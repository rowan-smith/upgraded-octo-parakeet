using System.Text.Json;
using ForgeDeck.Build;
using ForgeDeck.Build.Enterprise;
using ForgeDeck.Build.Team;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Licensing;

namespace Build.Enterprise.Tests;

public sealed class BuildEnterpriseCapabilityGrantMatrixTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public static IEnumerable<object[]> TeamCapabilities() =>
        KnownCapabilities.Build.Team.Select(c => new object[] { c });

    [Fact]
    public void Enterprise_capabilities_not_shipped_yet() =>
        Assert.Empty(KnownCapabilities.Build.Enterprise);

    [Theory]
    [MemberData(nameof(TeamCapabilities))]
    public void Enterprise_sku_still_grants_team_capabilities_with_team_package(string capability)
    {
        var service = CreateService("BUILD-ENTERPRISE",
            [new BuildModule(), new BuildTeamModule(), new BuildEnterpriseModule()]);
        Assert.True(service.Has(OrganisationId, capability));
    }

    private static CapabilityService CreateService(string sku, IPlatformModule[] modules)
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = LicenceSkuCatalog.CreateDocument(sku, OrganisationId);
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var entitlement = SignedLicenceEntitlementStore.ParseVerified(json, keys)!;
        return new CapabilityService(modules, new SignedLicenceEntitlementStore(entitlement));
    }
}
