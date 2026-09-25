using System.Text.Json;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Licensing;

namespace E2E.Playwright.Tests;

/// <summary>Creates signed commercial licence payloads for Playwright hosts that override Licensing:PublicKeyPem.</summary>
internal sealed class TestLicenceFactory : IDisposable
{
    private readonly LicenceKeyPair _keys = LicenceCryptography.CreateKeyPair();

    public string PublicKeyPem => _keys.PublicKeyPem;

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
        document.Signature = LicenceCryptography.Sign(document, _keys);
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

    public void Dispose()
    {
    }
}
