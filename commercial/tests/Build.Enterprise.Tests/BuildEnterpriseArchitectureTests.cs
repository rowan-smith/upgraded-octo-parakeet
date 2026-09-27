using System.Text.Json;
using ForgeDeck.Build;
using ForgeDeck.Build.Enterprise;
using ForgeDeck.Build.Team;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Licensing;

namespace Build.Enterprise.Tests;

public sealed class BuildEnterpriseArchitectureTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Enterprise_depends_on_team()
    {
        var root = FindRepoRoot();
        var csproj = Path.Combine(root, "commercial", "enterprise", "Modules", "Build.Enterprise",
            "ForgeDeck.Build.Enterprise", "ForgeDeck.Build.Enterprise.csproj");
        var text = File.ReadAllText(csproj);
        Assert.Contains("ForgeDeck.Build.Team", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Enterprise_capabilities_not_shipped_yet()
    {
        Assert.Empty(KnownCapabilities.Build.Enterprise);
        Assert.Empty(new BuildEnterpriseModule().Manifest.Capabilities);
    }

    [Fact]
    public void Enterprise_sku_still_grants_team_soft_unlock_with_team_package()
    {
        var entitlement = CreateVerifiedEntitlement(
            OrganisationId,
            "Enterprise",
            [KnownCapabilities.Build.Concurrent]);

        var withEnterprise = new CapabilityService(
            [new BuildModule(), new BuildTeamModule(), new BuildEnterpriseModule()],
            new SignedLicenceEntitlementStore(entitlement));
        Assert.True(withEnterprise.Has(OrganisationId, KnownCapabilities.Build.Concurrent));
        Assert.False(withEnterprise.Has(OrganisationId, KnownCapabilities.Build.Attestation));
    }

    private static OrganisationLicenceEntitlement CreateVerifiedEntitlement(
        Guid organisationId, string edition, string[] capabilities)
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = new LicenceDocument
        {
            OrganisationId = organisationId,
            IssuedAt = DateTimeOffset.Parse("2026-09-20T00:00:00Z"),
            Modules =
            {
                ["build"] = new LicenceModuleDocument
                {
                    Edition = edition,
                    Capabilities = [..capabilities]
                }
            }
        };
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return SignedLicenceEntitlementStore.ParseVerified(json, keys)!;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ForgeDeck.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
