using System.Security.Cryptography;
using System.Text.Json;
using ForgeDeck.Build;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Git;
using ForgeDeck.Review;
using ForgeDeck.Review.Domain;

namespace Core.Tests;

public sealed class LicensingTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Community_modules_do_not_declare_commercial_capabilities()
    {
        IPlatformModule[] modules = [new ReviewModule(), new GitModule(), new BuildModule()];

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
        Assert.DoesNotContain("ForgeDeck.Review.Enterprise", typeof(ReviewModule).Assembly.GetReferencedAssemblies().Select(r => r.Name));
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
        var keys = LicenceCryptography.CreateKeyPair();
        var document = CreateDocument();
        document.Signature = "not-a-real-signature";
        var json = JsonSerializer.Serialize(document);

        Assert.ThrowsAny<Exception>(() =>
            SignedLicenceEntitlementStore.ParseVerified(json, keys));
    }

    [Fact]
    public void Signed_licence_without_commercial_package_does_not_activate_capability()
    {
        var entitlement = CreateVerifiedEntitlement();
        var service = new CapabilityService([new ReviewModule()], new SignedLicenceEntitlementStore(entitlement));

        Assert.False(service.Has(OrganisationId, KnownCapabilities.Review.MultiApproval));
    }

    [Fact]
    public void Multi_approval_policy_requires_licence_to_configure()
    {
        var capabilities = CreateCommunityService();
        var authorizer = new CapabilityAuthorizer(capabilities, new ForgeDeck.Core.Context.PlatformContextStore());
        var resolver = new ApprovalPolicyResolver(
            capabilities,
            new ForgeDeck.Core.Context.PlatformContextStore(),
            new ReviewPolicyState(),
            []);

        var exception = Assert.Throws<LicenceRequiredException>(() => resolver.Configure(2, authorizer));
        Assert.Equal(KnownCapabilities.Review.MultiApproval, exception.Capability);
        Assert.Equal(nameof(SingleApprovalPolicy), resolver.Resolve().GetType().Name);
    }

    [Fact]
    public void Licence_cryptography_round_trips()
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = CreateDocument();
        document.Signature = LicenceCryptography.Sign(document, keys);
        Assert.True(LicenceCryptography.Verify(document, keys));

        document.Modules["review"].Capabilities.Add("Review.TeamApproval");
        Assert.False(LicenceCryptography.Verify(document, keys));
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
        var keys = LicenceCryptography.CreateKeyPair();
        var document = CreateDocument();
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return SignedLicenceEntitlementStore.ParseVerified(json, keys)!;
    }
}
