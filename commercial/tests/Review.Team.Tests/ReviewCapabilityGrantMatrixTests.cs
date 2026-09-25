using System.Text.Json;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Review;
using ForgeDeck.Review.Team;

namespace Review.Team.Tests;

public sealed class ReviewCapabilityGrantMatrixTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public static IEnumerable<object[]> TeamCapabilities() =>
        KnownCapabilities.Review.Team.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(TeamCapabilities))]
    public void Team_sku_and_package_grants_each_team_capability(string capability)
    {
        var service = CreateService("REVIEW-TEAM", [new ReviewModule(), new ReviewTeamModule()]);
        Assert.True(service.Has(OrganisationId, capability));
    }

    [Theory]
    [MemberData(nameof(TeamCapabilities))]
    public void Team_capability_absent_without_licence(string capability)
    {
        var service = new CapabilityService(
            [new ReviewModule(), new ReviewTeamModule()],
            new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null));
        Assert.False(service.Has(OrganisationId, capability));
    }

    [Theory]
    [MemberData(nameof(TeamCapabilities))]
    public void Team_capability_absent_without_package(string capability)
    {
        var service = CreateService("REVIEW-TEAM", [new ReviewModule()]);
        Assert.False(service.Has(OrganisationId, capability));
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
