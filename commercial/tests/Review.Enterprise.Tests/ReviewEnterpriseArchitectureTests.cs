using System.Security.Cryptography;
using System.Text.Json;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Extensions;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Review;
using ForgeDeck.Review.Enterprise;
using ForgeDeck.Review.Team;

namespace Review.Enterprise.Tests;

public sealed class ReviewEnterpriseArchitectureTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Enterprise_depends_on_team()
    {
        var root = FindRepoRoot();
        var csproj = Path.Combine(root, "commercial", "enterprise", "Modules", "Review.Enterprise",
            "ForgeDeck.Review.Enterprise", "ForgeDeck.Review.Enterprise.csproj");
        var text = File.ReadAllText(csproj);
        Assert.Contains("ForgeDeck.Review.Team", text, StringComparison.Ordinal);
        Assert.Contains("ForgeDeck.Contracts", text, StringComparison.Ordinal);
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

    [Fact]
    public void Enterprise_declares_enterprise_capabilities_only()
    {
        var caps = new ReviewEnterpriseModule().Manifest.Capabilities;
        Assert.Contains(KnownCapabilities.Review.SeparationOfDuties, caps);
        Assert.DoesNotContain(KnownCapabilities.Review.MultiApproval, caps);
        Assert.DoesNotContain(KnownCapabilities.Review.CodeOwners, caps);
    }

    [Fact]
    public void Enterprise_capability_requires_enterprise_package_and_licence()
    {
        var entitlement = CreateVerifiedEntitlement(
            OrganisationId,
            "Enterprise",
            [
                KnownCapabilities.Review.MultiApproval,
                KnownCapabilities.Review.CodeOwners,
                KnownCapabilities.Review.SeparationOfDuties
            ]);

        var withoutEnterprise = new CapabilityService(
            [new ReviewModule(), new ReviewTeamModule()],
            new SignedLicenceEntitlementStore(entitlement));
        Assert.False(withoutEnterprise.Has(OrganisationId, KnownCapabilities.Review.SeparationOfDuties));
        Assert.True(withoutEnterprise.Has(OrganisationId, KnownCapabilities.Review.MultiApproval));
        Assert.True(withoutEnterprise.Has(OrganisationId, KnownCapabilities.Review.CodeOwners));

        var withEnterprise = new CapabilityService(
            [new ReviewModule(), new ReviewTeamModule(), new ReviewEnterpriseModule()],
            new SignedLicenceEntitlementStore(entitlement));
        Assert.True(withEnterprise.Has(OrganisationId, KnownCapabilities.Review.SeparationOfDuties));
    }

    [Fact]
    public void Tampered_commercial_package_fails_signature_verification()
    {
        using var rsa = RSA.Create(2048);
        var envelope = new ExtensionPackageEnvelope(
            "forgedeck.review.enterprise",
            "0.1.0",
            "abc123digest",
            null);
        var signature = SignedExtensionPackageVerifier.Sign(envelope, rsa);
        var signed = envelope with { SignatureBase64 = signature };
        var tampered = signed with { ContentDigestSha256 = "tampered" };

        using var verifier = new SignedExtensionPackageVerifier(rsa);
        Assert.True(verifier.Verify(signed).Success);
        Assert.False(verifier.Verify(tampered).Success);
        Assert.Equal("Package signature verification failed.", verifier.Verify(tampered).Error);
    }

    [Fact]
    public void Instance_bound_licence_rejects_mismatched_instance()
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = new LicenceDocument
        {
            OrganisationId = OrganisationId,
            IssuedAt = DateTimeOffset.UtcNow,
            InstanceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Modules =
            {
                ["review"] = new LicenceModuleDocument
                {
                    Edition = "Enterprise",
                    Capabilities = [KnownCapabilities.Review.CodeOwners]
                }
            }
        };
        document.Signature = LicenceCryptography.Sign(document, keys);
        Assert.True(LicenceCryptography.Verify(document, keys));
        Assert.NotEqual(Guid.Empty, document.InstanceId);
        Assert.NotEqual(document.InstanceId, OrganisationId);
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
