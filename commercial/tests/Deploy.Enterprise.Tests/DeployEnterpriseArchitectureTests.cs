using System.Text.Json;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Deploy;
using ForgeDeck.Deploy.Enterprise;
using ForgeDeck.Deploy.Team;

namespace Deploy.Enterprise.Tests;

public sealed class DeployEnterpriseArchitectureTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Enterprise_depends_on_team()
    {
        var root = FindRepoRoot();
        var csproj = Path.Combine(root, "commercial", "enterprise", "Modules", "Deploy.Enterprise",
            "ForgeDeck.Deploy.Enterprise", "ForgeDeck.Deploy.Enterprise.csproj");
        var text = File.ReadAllText(csproj);
        Assert.Contains("ForgeDeck.Deploy.Team", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Enterprise_declares_enterprise_capabilities_only()
    {
        var caps = new DeployEnterpriseModule().Manifest.Capabilities;
        Assert.Contains(KnownCapabilities.Deploy.MultiSite, caps);
        Assert.DoesNotContain(KnownCapabilities.Deploy.MultiEnvironment, caps);
    }

    [Fact]
    public void Enterprise_capability_requires_enterprise_package_and_licence()
    {
        var entitlement = CreateVerifiedEntitlement(
            OrganisationId,
            "Enterprise",
            [
                KnownCapabilities.Deploy.MultiEnvironment,
                KnownCapabilities.Deploy.MultiSite
            ]);

        var withoutEnterprise = new CapabilityService(
            [new DeployModule(), new DeployTeamModule()],
            new SignedLicenceEntitlementStore(entitlement));
        Assert.False(withoutEnterprise.Has(OrganisationId, KnownCapabilities.Deploy.MultiSite));
        Assert.True(withoutEnterprise.Has(OrganisationId, KnownCapabilities.Deploy.MultiEnvironment));

        var withEnterprise = new CapabilityService(
            [new DeployModule(), new DeployTeamModule(), new DeployEnterpriseModule()],
            new SignedLicenceEntitlementStore(entitlement));
        Assert.True(withEnterprise.Has(OrganisationId, KnownCapabilities.Deploy.MultiSite));
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
                ["deploy"] = new LicenceModuleDocument
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
