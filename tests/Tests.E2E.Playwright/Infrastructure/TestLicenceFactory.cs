using System.Security.Cryptography;
using System.Text.Json;
using Platform.Contracts.Capabilities;
using Platform.Contracts.Licensing;
using Platform.Core.Domain;
using Platform.Core.Licensing;

namespace Tests.E2E.Playwright;

/// <summary>Creates signed commercial licence payloads for Playwright hosts that override Licensing:PublicKeyPem.</summary>
internal sealed class TestLicenceFactory : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);

    public string PublicKeyPem => _rsa.ExportSubjectPublicKeyInfoPem();

    public string CreateCommercialJson(
        Guid? organisationId = null,
        IEnumerable<string>? reviewCapabilities = null,
        DateTimeOffset? expiresAt = null)
    {
        var document = new LicenceDocument
        {
            OrganisationId = organisationId ?? KnownIds.OrganisationId,
            IssuedAt = DateTimeOffset.UtcNow,
            Modules =
            {
                ["review"] = new LicenceModuleDocument
                {
                    Edition = "Commercial",
                    Capabilities = (reviewCapabilities ?? [KnownCapabilities.Review.MultiApproval]).ToList(),
                    ExpiresAt = expiresAt
                }
            }
        };
        document.Signature = LicenceCryptography.Sign(document, _rsa);
        return JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    public string CreateTamperedJson()
    {
        var json = CreateCommercialJson();
        var document = JsonSerializer.Deserialize<LicenceDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
        document.Modules["review"].Capabilities.Add(KnownCapabilities.Review.TeamApproval);
        // Keep the old signature so verification fails.
        return JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    public void Dispose() => _rsa.Dispose();
}
