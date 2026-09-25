using System.Text.Json;
using ForgeDeck.Build;
using ForgeDeck.Build.Team;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Licensing;

namespace Build.Team.Tests;

public sealed class BuildTeamTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Team_depends_on_community_build()
    {
        var refs = typeof(BuildTeamModule).Assembly.GetReferencedAssemblies().Select(r => r.Name).ToArray();
        Assert.Contains("ForgeDeck.Build", refs);
        Assert.DoesNotContain("ForgeDeck.Build.Enterprise", refs);
    }

    [Fact]
    public void Team_package_without_licence_does_not_grant_concurrent()
    {
        var service = new CapabilityService(
            [new BuildModule(), new BuildTeamModule()],
            new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null));

        Assert.False(service.Has(OrganisationId, KnownCapabilities.Build.Concurrent));
    }

    [Fact]
    public void Team_licence_with_package_grants_concurrent()
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = LicenceSkuCatalog.CreateDocument("BUILD-TEAM", OrganisationId);
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var entitlement = SignedLicenceEntitlementStore.ParseVerified(json, keys)!;
        var service = new CapabilityService(
            [new BuildModule(), new BuildTeamModule()],
            new SignedLicenceEntitlementStore(entitlement));

        Assert.True(service.Has(OrganisationId, KnownCapabilities.Build.Concurrent));
        Assert.True(service.Has(OrganisationId, KnownCapabilities.Build.Schedules));
    }
}
