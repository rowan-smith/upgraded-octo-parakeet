using System.Security.Cryptography;
using System.Text.Json;
using Modules.Git;
using Modules.Pipelines;
using Modules.Review;
using Modules.Review.Commercial;
using Modules.Review.Domain;
using Platform.Contracts.Capabilities;
using Platform.Contracts.Licensing;
using Platform.Contracts.Modules;
using Platform.Core.Capabilities;
using Platform.Core.Licensing;

namespace Tests.Unit;

public sealed class LicensingTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Community_modules_do_not_declare_commercial_capabilities()
    {
        IPlatformModule[] modules = [new ReviewModule(), new GitModule(), new PipelinesModule()];

        foreach (var module in modules)
        {
            Assert.Equal("Community", module.Manifest.Edition);
            Assert.DoesNotContain(module.Manifest.Capabilities, KnownCapabilities.IsCommercial);
        }
    }

    [Fact]
    public void Community_assembly_does_not_contain_commercial_policy()
    {
        var reviewTypes = typeof(ReviewModule).Assembly.GetTypes().Select(type => type.Name);
        Assert.DoesNotContain("MinimumApprovalsPolicy", reviewTypes);
        Assert.DoesNotContain("Modules.Review.Commercial", typeof(ReviewModule).Assembly.GetReferencedAssemblies().Select(r => r.Name));
    }

    [Fact]
    public void Organisation_receives_installed_community_capabilities_by_default()
    {
        var service = CreateCommunityService();

        Assert.True(service.Has(OrganisationId, KnownCapabilities.Review.BasicApproval));
        Assert.False(service.Has(OrganisationId, KnownCapabilities.Review.MultiApproval));
        Assert.Equal("Community", service.EditionFor(OrganisationId, "review", "Community"));
    }

    [Fact]
    public void Unsigned_or_tampered_licence_does_not_grant_commercial_capabilities()
    {
        using var rsa = RSA.Create(2048);
        var document = CreateDocument();
        document.Signature = "not-a-real-signature";
        var json = JsonSerializer.Serialize(document);

        Assert.ThrowsAny<Exception>(() =>
            SignedLicenceEntitlementStore.ParseVerified(json, rsa));
    }

    [Fact]
    public void Missing_licence_cannot_unlock_commercial_capabilities_even_with_commercial_package()
    {
        // Even with commercial module present, missing/invalid entitlement store grants nothing commercial.
        var service = new CapabilityService(
            [new ReviewModule(), new ReviewCommercialModule()],
            new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null));

        Assert.True(service.Has(OrganisationId, KnownCapabilities.Review.BasicApproval));
        Assert.False(service.Has(OrganisationId, KnownCapabilities.Review.MultiApproval));
    }

    [Fact]
    public void Signed_licence_without_commercial_package_does_not_activate_capability()
    {
        var entitlement = CreateVerifiedEntitlement();
        var service = new CapabilityService([new ReviewModule()], new SignedLicenceEntitlementStore(entitlement));

        Assert.False(service.Has(OrganisationId, KnownCapabilities.Review.MultiApproval));
    }

    [Fact]
    public void Signed_licence_with_commercial_package_grants_capability()
    {
        var entitlement = CreateVerifiedEntitlement();
        var service = new CapabilityService(
            [new ReviewModule(), new ReviewCommercialModule()],
            new SignedLicenceEntitlementStore(entitlement));

        Assert.True(service.Has(OrganisationId, KnownCapabilities.Review.BasicApproval));
        Assert.True(service.Has(OrganisationId, KnownCapabilities.Review.MultiApproval));
        Assert.Equal("Commercial", service.EditionFor(OrganisationId, "review", "Community"));
        Assert.False(service.Has(Guid.NewGuid(), KnownCapabilities.Review.MultiApproval));
    }

    [Fact]
    public void Multi_approval_policy_requires_licence_to_configure()
    {
        var capabilities = CreateCommunityService();
        var authorizer = new CapabilityAuthorizer(capabilities, new Platform.Core.Context.PlatformContextStore());
        var resolver = new ApprovalPolicyResolver(
            capabilities,
            new Platform.Core.Context.PlatformContextStore(),
            new ReviewPolicyState(),
            []);

        var exception = Assert.Throws<LicenceRequiredException>(() => resolver.Configure(2, authorizer));
        Assert.Equal(KnownCapabilities.Review.MultiApproval, exception.Capability);
        Assert.Equal(nameof(SingleApprovalPolicy), resolver.Resolve().GetType().Name);
    }

    [Fact]
    public void Licensed_multi_approval_uses_commercial_policy_factory()
    {
        var entitlement = CreateVerifiedEntitlement();
        var capabilities = new CapabilityService(
            [new ReviewModule(), new ReviewCommercialModule()],
            new SignedLicenceEntitlementStore(entitlement));
        var context = new Platform.Core.Context.PlatformContextStore();
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
            Repository = new Platform.Contracts.SourceControl.SourceRepository("github", "org", "repo", "main"),
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
    public void Licence_cryptography_round_trips()
    {
        using var rsa = RSA.Create(2048);
        var document = CreateDocument();
        document.Signature = LicenceCryptography.Sign(document, rsa);
        Assert.True(LicenceCryptography.Verify(document, rsa));

        document.Modules["review"].Capabilities.Add("Review.TeamApproval");
        Assert.False(LicenceCryptography.Verify(document, rsa));
    }

    private static CapabilityService CreateCommunityService() =>
        new([new ReviewModule()], new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null));

    private static LicenceDocument CreateDocument() => new()
    {
        OrganisationId = OrganisationId,
        IssuedAt = DateTimeOffset.Parse("2026-09-20T00:00:00Z"),
        Modules =
        {
            ["review"] = new LicenceModuleDocument
            {
                Edition = "Commercial",
                Capabilities = [KnownCapabilities.Review.MultiApproval]
            }
        }
    };

    private static OrganisationLicenceEntitlement CreateVerifiedEntitlement()
    {
        using var rsa = RSA.Create(2048);
        var document = CreateDocument();
        document.Signature = LicenceCryptography.Sign(document, rsa);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return SignedLicenceEntitlementStore.ParseVerified(json, rsa)!;
    }
}
