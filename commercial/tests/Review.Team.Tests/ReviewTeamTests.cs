using System.Security.Cryptography;
using System.Text.Json;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Review;
using ForgeDeck.Review.Domain;
using ForgeDeck.Review.Team;

namespace Review.Team.Tests;

public sealed class ReviewTeamTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Team_depends_on_community_review()
    {
        var refs = typeof(ReviewTeamModule).Assembly.GetReferencedAssemblies().Select(r => r.Name).ToArray();
        Assert.Contains("ForgeDeck.Review", refs);
        Assert.DoesNotContain("ForgeDeck.Review.Enterprise", refs);
    }

    [Fact]
    public void Team_package_without_licence_does_not_grant_multi_approval()
    {
        var service = new CapabilityService(
            [new ReviewModule(), new ReviewTeamModule()],
            new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null));

        Assert.True(service.Has(OrganisationId, KnownCapabilities.Review.BasicApproval));
        Assert.False(service.Has(OrganisationId, KnownCapabilities.Review.MultiApproval));
    }

    [Fact]
    public void Team_licence_with_package_grants_multi_approval()
    {
        var entitlement = CreateVerifiedEntitlement(OrganisationId, "Team", [KnownCapabilities.Review.MultiApproval]);
        var service = new CapabilityService(
            [new ReviewModule(), new ReviewTeamModule()],
            new SignedLicenceEntitlementStore(entitlement));

        Assert.True(service.Has(OrganisationId, KnownCapabilities.Review.MultiApproval));
        Assert.False(service.Has(OrganisationId, KnownCapabilities.Review.CodeOwners));
    }

    [Fact]
    public void Licensed_multi_approval_uses_team_policy_factory()
    {
        var entitlement = CreateVerifiedEntitlement(OrganisationId, "Team", [KnownCapabilities.Review.MultiApproval]);
        var capabilities = new CapabilityService(
            [new ReviewModule(), new ReviewTeamModule()],
            new SignedLicenceEntitlementStore(entitlement));
        var context = new ForgeDeck.Core.Context.PlatformContextStore();
        var authorizer = new CapabilityAuthorizer(capabilities, context);
        var resolver = new ApprovalPolicyResolver(
            capabilities,
            context,
            new ReviewPolicyState(),
            [new MultiApprovalPolicyFactory()]);
        resolver.Configure(2, authorizer);

        var change = new Change
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ExternalId = "1",
            ExternalNumber = 1,
            ExternalUrl = "https://github.com/org/repo/pull/1",
            Title = "Test",
            Description = "Test",
            Author = "A",
            SourceBranch = "feature",
            TargetBranch = "main",
            HeadCommit = "abc1234",
            BaseCommit = "def5678",
            Repository = new ForgeDeck.Contracts.SourceControl.SourceRepository("github", "org", "repo", "main"),
            ProviderMergeable = true
        };

        var policy = resolver.Resolve();
        Assert.Equal(nameof(MinimumApprovalsPolicy), policy.GetType().Name);
        change.Approve("Alex", policy);
        Assert.False(change.CanMergeWith(policy));
        change.Approve("Sam", policy);
        Assert.True(change.CanMergeWith(policy));
    }

    [Fact]
    public void Tier_only_sku_expands_to_team_capabilities_with_package()
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = LicenceSkuCatalog.CreateDocument("REVIEW-TEAM", OrganisationId);
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var entitlement = SignedLicenceEntitlementStore.ParseVerified(json, keys)!;
        var service = new CapabilityService(
            [new ReviewModule(), new ReviewTeamModule()],
            new SignedLicenceEntitlementStore(entitlement));
        Assert.True(service.Has(OrganisationId, KnownCapabilities.Review.MultiApproval));
        Assert.True(service.Has(OrganisationId, KnownCapabilities.Review.TeamApproval));
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
                ["review"] = new LicenceModuleDocument
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
}
