using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Contracts.Licensing;
using Platform.Core.Capabilities;

namespace Platform.Core.Licensing;

public sealed class SignedLicenceEntitlementStore : ILicenceEntitlementStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RSA _publicKey;
    private readonly OrganisationLicenceEntitlement? _entitlement;

    public SignedLicenceEntitlementStore(
        IOptions<LicensingOptions> options,
        IHostEnvironment environment,
        ILogger<SignedLicenceEntitlementStore> logger)
    {
        var pem = string.IsNullOrWhiteSpace(options.Value.PublicKeyPem)
            ? ForgeDeckLicenceKeys.PublicKeyPem
            : options.Value.PublicKeyPem;
        _publicKey = LicenceCryptography.ImportPublicKeyPem(pem);
        _entitlement = Load(options.Value.LicencePath, environment.ContentRootPath, logger);
    }

    /// <summary>Test/helper constructor that trusts an already-verified entitlement.</summary>
    public SignedLicenceEntitlementStore(OrganisationLicenceEntitlement? entitlement, RSA? publicKey = null)
    {
        _publicKey = publicKey ?? LicenceCryptography.ImportPublicKeyPem(ForgeDeckLicenceKeys.PublicKeyPem);
        _entitlement = entitlement;
    }

    public static OrganisationLicenceEntitlement? ParseVerified(string json, RSA publicKey)
    {
        var document = JsonSerializer.Deserialize<LicenceDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Licence file was empty.");
        if (!LicenceCryptography.Verify(document, publicKey))
            throw new CryptographicException("Licence signature is invalid.");

        var modules = new Dictionary<string, ModuleLicenceEntitlement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (moduleId, module) in document.Modules)
        {
            modules[moduleId] = new ModuleLicenceEntitlement(
                string.IsNullOrWhiteSpace(module.Edition) ? "Commercial" : module.Edition,
                module.Capabilities.ToArray(),
                module.ExpiresAt);
        }

        return new OrganisationLicenceEntitlement(document.OrganisationId, document.IssuedAt, modules);
    }

    public bool TryGet(Guid organisationId, out OrganisationLicenceEntitlement entitlement)
    {
        if (_entitlement is not null && _entitlement.OrganisationId == organisationId)
        {
            entitlement = _entitlement;
            return true;
        }

        entitlement = null!;
        return false;
    }

    public void Dispose() => _publicKey.Dispose();

    private OrganisationLicenceEntitlement? Load(string? relativePath, string contentRoot, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var path = Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(contentRoot, relativePath);
        if (!File.Exists(path))
        {
            logger.LogInformation("No commercial licence file at {Path}; Community capabilities only.", path);
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var entitlement = ParseVerified(json, _publicKey);
            logger.LogInformation("Loaded signed commercial licence for organisation {OrganisationId}.", entitlement!.OrganisationId);
            return entitlement;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to load commercial licence from {Path}; ignoring file.", path);
            return null;
        }
    }
}
