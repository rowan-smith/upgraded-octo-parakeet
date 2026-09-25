using System.Text.Json;
using ForgeDeck.Build;
using ForgeDeck.Build.Enterprise;
using ForgeDeck.Build.Team;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Licensing;

namespace Build.Enterprise.Tests;

public sealed class BuildEnterpriseCapabilityGrantMatrixTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public static IEnumerable<object[]> EnterpriseCapabilities() =>
        KnownCapabilities.Build.Enterprise.Select(c => new object[] { c });

    public static IEnumerable<object[]> TeamCapabilities() =>
        KnownCapabilities.Build.Team.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(EnterpriseCapabilities))]
    public void Enterprise_sku_and_packages_grant_enterprise_capability(string capability)
    {
        var service = CreateService("BUILD-ENTERPRISE",
            [new BuildModule(), new BuildTeamModule(), new BuildEnterpriseModule()]);
        Assert.True(service.Has(OrganisationId, capability));
    }

    [Theory]
    [MemberData(nameof(EnterpriseCapabilities))]
    public void Enterprise_capability_absent_without_enterprise_package(string capability)
    {
        var service = CreateService("BUILD-ENTERPRISE", [new BuildModule(), new BuildTeamModule()]);
        Assert.False(service.Has(OrganisationId, capability));
    }

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
