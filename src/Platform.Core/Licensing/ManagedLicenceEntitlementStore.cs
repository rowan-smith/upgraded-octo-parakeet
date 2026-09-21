using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Contracts.Licensing;
using Platform.Core.Capabilities;
using Platform.Core.Domain;
using Platform.Core.Persistence;

namespace Platform.Core.Licensing;

/// <summary>
/// Loads entitlements from the active DB licence (preferred) or the legacy file path.
/// Supports runtime install/replace without restarting the process.
/// </summary>
public sealed class ManagedLicenceEntitlementStore : ILicenceEntitlementStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RSA _publicKey;
    private readonly ITenancyStore _store;
    private readonly object _gate = new();
    private OrganisationLicenceEntitlement? _entitlement;

    public ManagedLicenceEntitlementStore(
        ITenancyStore store,
        IOptions<LicensingOptions> options,
        IHostEnvironment environment,
        ILogger<ManagedLicenceEntitlementStore> logger)
    {
        _store = store;
        var pem = string.IsNullOrWhiteSpace(options.Value.PublicKeyPem)
            ? ForgeDeckLicenceKeys.PublicKeyPem
            : options.Value.PublicKeyPem;
        _publicKey = LicenceCryptography.ImportPublicKeyPem(pem);
        _entitlement = LoadFromDatabase() ?? LoadFromFile(options.Value.LicencePath, environment.ContentRootPath, logger);
    }

    public ManagedLicenceEntitlementStore(OrganisationLicenceEntitlement? entitlement, RSA? publicKey = null)
    {
        _store = null!;
        _publicKey = publicKey ?? LicenceCryptography.ImportPublicKeyPem(ForgeDeckLicenceKeys.PublicKeyPem);
        _entitlement = entitlement;
    }

    public RSA PublicKey => _publicKey;

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
        lock (_gate)
        {
            if (_entitlement is not null && _entitlement.OrganisationId == organisationId)
            {
                entitlement = _entitlement;
                return true;
            }
        }

        entitlement = null!;
        return false;
    }

    public void SetEntitlement(OrganisationLicenceEntitlement? entitlement)
    {
        lock (_gate) _entitlement = entitlement;
    }

    public void ClearEntitlement() => SetEntitlement(null);

    public void Dispose() => _publicKey.Dispose();

    private OrganisationLicenceEntitlement? LoadFromDatabase()
    {
        try
        {
            var active = _store.GetActiveLicence();
            if (active is null || active.Mode != LicenceMode.Commercial || string.IsNullOrWhiteSpace(active.Payload))
                return null;
            return ParseVerified(active.Payload, _publicKey);
        }
        catch
        {
            return null;
        }
    }

    private OrganisationLicenceEntitlement? LoadFromFile(string? relativePath, string contentRoot, ILogger logger)
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

/// <summary>Backward-compatible alias used by existing tests.</summary>
public sealed class SignedLicenceEntitlementStore : ILicenceEntitlementStore, IDisposable
{
    private readonly ManagedLicenceEntitlementStore _inner;

    public SignedLicenceEntitlementStore(
        IOptions<LicensingOptions> options,
        IHostEnvironment environment,
        ILogger<SignedLicenceEntitlementStore> logger,
        ITenancyStore store)
    {
        _inner = new ManagedLicenceEntitlementStore(store, options,
            environment,
            new LoggerAdapter(logger));
    }

    public SignedLicenceEntitlementStore(OrganisationLicenceEntitlement? entitlement, RSA? publicKey = null) =>
        _inner = new ManagedLicenceEntitlementStore(entitlement, publicKey);

    public static OrganisationLicenceEntitlement? ParseVerified(string json, RSA publicKey) =>
        ManagedLicenceEntitlementStore.ParseVerified(json, publicKey);

    public bool TryGet(Guid organisationId, out OrganisationLicenceEntitlement entitlement) =>
        _inner.TryGet(organisationId, out entitlement);

    public void Dispose() => _inner.Dispose();

    private sealed class LoggerAdapter(ILogger logger) : ILogger<ManagedLicenceEntitlementStore>
    {
        IDisposable? ILogger.BeginScope<TState>(TState state) => logger.BeginScope(state);
        bool ILogger.IsEnabled(LogLevel logLevel) => logger.IsEnabled(logLevel);
        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            logger.Log(logLevel, eventId, state, exception, formatter);
    }
}
