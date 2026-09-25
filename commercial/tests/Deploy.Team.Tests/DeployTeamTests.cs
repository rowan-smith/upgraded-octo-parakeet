using System.Text.Json;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Deploy;
using ForgeDeck.Deploy.Team;

namespace Deploy.Team.Tests;

public sealed class DeployTeamTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Team_depends_on_community_deploy()
    {
        var refs = typeof(DeployTeamModule).Assembly.GetReferencedAssemblies().Select(r => r.Name).ToArray();
        Assert.Contains("ForgeDeck.Deploy", refs);
        Assert.DoesNotContain("ForgeDeck.Deploy.Enterprise", refs);
    }

    [Fact]
    public void Team_package_without_licence_does_not_grant_multi_environment()
    {
        var service = new CapabilityService(
            [new DeployModule(), new DeployTeamModule()],
            new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null));

        Assert.False(service.Has(OrganisationId, KnownCapabilities.Deploy.MultiEnvironment));
    }

    [Fact]
    public void Team_licence_with_package_grants_multi_environment()
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = LicenceSkuCatalog.CreateDocument("DEPLOY-TEAM", OrganisationId);
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var entitlement = SignedLicenceEntitlementStore.ParseVerified(json, keys)!;
        var service = new CapabilityService(
            [new DeployModule(), new DeployTeamModule()],
            new SignedLicenceEntitlementStore(entitlement));

        Assert.True(service.Has(OrganisationId, KnownCapabilities.Deploy.MultiEnvironment));
        Assert.True(service.Has(OrganisationId, KnownCapabilities.Deploy.PromotionPolicy));
    }
}
