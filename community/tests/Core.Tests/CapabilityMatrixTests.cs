using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Contracts.Topology;
using ForgeDeck.Core.Licensing;

namespace Core.Tests;

/// <summary>
/// Exhaustive matrices over commercial capabilities, SKUs, modules, and deployment profiles.
/// </summary>
public sealed class CapabilityMatrixTests
{
    public static IEnumerable<object[]> AllCommercialCapabilities()
    {
        foreach (var cap in KnownCapabilities.AllCommercial)
        {
            yield return [cap];
        }
    }

    public static IEnumerable<object[]> CapabilityLevelPairs()
    {
        foreach (var cap in KnownCapabilities.AllCommercial)
        {
            var min = ModuleEntitlementTables.MinimumLevelFor(cap);
            yield return [cap, EntitlementLevel.Community, false];
            yield return [cap, EntitlementLevel.Team, min <= EntitlementLevel.Team];
            yield return [cap, EntitlementLevel.Enterprise, true];
        }
    }

    public static IEnumerable<object[]> ModuleLevelPairs()
    {
        foreach (var module in PlatformModules.All)
        {
            yield return [module, EntitlementLevel.Community];
            yield return [module, EntitlementLevel.Team];
            yield return [module, EntitlementLevel.Enterprise];
        }
    }

    [Theory]
    [MemberData(nameof(AllCommercialCapabilities))]
    public void Commercial_capability_is_flagged_commercial(string capability) =>
        Assert.True(KnownCapabilities.IsCommercial(capability));

    [Theory]
    [MemberData(nameof(AllCommercialCapabilities))]
    public void Commercial_capability_has_module_prefix(string capability)
    {
        var prefix = capability.Split('.')[0];
        Assert.Contains(prefix, new[] { "Review", "Build", "Deploy", "Git", "Code" });
    }

    [Theory]
    [MemberData(nameof(CapabilityLevelPairs))]
    public void Entitlement_table_grants_capability_at_expected_levels(
        string capability, EntitlementLevel level, bool expected)
    {
        var module = capability.Split('.')[0].ToLowerInvariant();
        var granted = ModuleEntitlementTables.CapabilitiesFor(module, level);
        Assert.Equal(expected, granted.Contains(capability));
    }

    [Theory]
    [MemberData(nameof(ModuleLevelPairs))]
    public void Community_level_never_grants_commercial_capabilities(string module, EntitlementLevel level)
    {
        if (level != EntitlementLevel.Community)
        {
            return;
        }

        Assert.Empty(ModuleEntitlementTables.CapabilitiesFor(module, level));
    }

    [Theory]
    [MemberData(nameof(ModuleLevelPairs))]
    public void Team_level_grants_only_team_caps(string module, EntitlementLevel level)
    {
        if (level != EntitlementLevel.Team)
        {
            return;
        }

        var granted = ModuleEntitlementTables.CapabilitiesFor(module, level);
        Assert.All(granted, cap =>
            Assert.Equal(EntitlementLevel.Team, ModuleEntitlementTables.MinimumLevelFor(cap)));
    }

    [Theory]
    [MemberData(nameof(ModuleLevelPairs))]
    public void Enterprise_includes_team_caps(string module, EntitlementLevel level)
    {
        if (level != EntitlementLevel.Enterprise)
        {
            return;
        }

        var team = ModuleEntitlementTables.CapabilitiesFor(module, EntitlementLevel.Team);
        var enterprise = ModuleEntitlementTables.CapabilitiesFor(module, EntitlementLevel.Enterprise);
        Assert.All(team, cap => Assert.Contains(cap, enterprise));
    }

    [Theory]
    [InlineData(KnownCapabilities.Review.BasicApproval, false)]
    [InlineData("Pipelines.BasicExecution", false)]
    [InlineData("Deploy.Basic", false)]
    [InlineData(KnownCapabilities.Build.Concurrent, true)]
    [InlineData(KnownCapabilities.Deploy.MultiEnvironment, true)]
    public void IsCommercial_matches_expectation(string capability, bool expected) =>
        Assert.Equal(expected, KnownCapabilities.IsCommercial(capability));
}

public sealed class LicenceSkuMatrixTests
{
    public static IEnumerable<object[]> SkuExpectations()
    {
        yield return ["TEAM-BUNDLE", PlatformModules.Review, EntitlementLevel.Team];
        yield return ["TEAM-BUNDLE", PlatformModules.Build, EntitlementLevel.Team];
        yield return ["TEAM-BUNDLE", PlatformModules.Deploy, EntitlementLevel.Team];
        yield return ["TEAM-BUNDLE", PlatformModules.Git, EntitlementLevel.Team];
        yield return ["TEAM-BUNDLE", PlatformModules.Code, EntitlementLevel.Team];
        yield return ["ENTERPRISE-BUNDLE", PlatformModules.Review, EntitlementLevel.Enterprise];
        yield return ["ENTERPRISE-BUNDLE", PlatformModules.Build, EntitlementLevel.Enterprise];
        yield return ["ENTERPRISE-BUNDLE", PlatformModules.Deploy, EntitlementLevel.Enterprise];
        yield return ["ENTERPRISE-BUNDLE", PlatformModules.Git, EntitlementLevel.Enterprise];
        yield return ["ENTERPRISE-BUNDLE", PlatformModules.Code, EntitlementLevel.Enterprise];
        yield return ["REVIEW-TEAM", PlatformModules.Review, EntitlementLevel.Team];
        yield return ["REVIEW-TEAM", PlatformModules.Build, EntitlementLevel.Community];
        yield return ["REVIEW-ENTERPRISE", PlatformModules.Review, EntitlementLevel.Enterprise];
        yield return ["BUILD-TEAM", PlatformModules.Build, EntitlementLevel.Team];
        yield return ["BUILD-TEAM", PlatformModules.Deploy, EntitlementLevel.Community];
        yield return ["BUILD-ENTERPRISE", PlatformModules.Build, EntitlementLevel.Enterprise];
        yield return ["DEPLOY-TEAM", PlatformModules.Deploy, EntitlementLevel.Team];
        yield return ["DEPLOY-TEAM", PlatformModules.Build, EntitlementLevel.Community];
        yield return ["DEPLOY-ENTERPRISE", PlatformModules.Deploy, EntitlementLevel.Enterprise];
        yield return ["GIT-TEAM", PlatformModules.Git, EntitlementLevel.Team];
        yield return ["GIT-ENTERPRISE", PlatformModules.Git, EntitlementLevel.Enterprise];
        yield return ["CODE-TEAM", PlatformModules.Code, EntitlementLevel.Team];
        yield return ["CODE-ENTERPRISE", PlatformModules.Code, EntitlementLevel.Enterprise];
        yield return ["BUILD-DEPLOY-TEAM", PlatformModules.Build, EntitlementLevel.Team];
        yield return ["BUILD-DEPLOY-TEAM", PlatformModules.Deploy, EntitlementLevel.Team];
        yield return ["BUILD-DEPLOY-TEAM", PlatformModules.Review, EntitlementLevel.Community];
    }

    [Theory]
    [MemberData(nameof(SkuExpectations))]
    public void Sku_expands_module_to_expected_level(string sku, string module, EntitlementLevel expected)
    {
        var map = LicenceSkuCatalog.Expand(sku);
        Assert.Equal(expected, map[module]);
    }

    [Theory]
    [InlineData("UNKNOWN-SKU")]
    [InlineData("")]
    [InlineData("team")]
    public void Unknown_sku_throws(string sku) =>
        Assert.Throws<ArgumentException>(() => LicenceSkuCatalog.Expand(sku));

    [Theory]
    [InlineData("TEAM-BUNDLE")]
    [InlineData("ENTERPRISE-BUNDLE")]
    [InlineData("BUILD-TEAM")]
    [InlineData("DEPLOY-TEAM")]
    [InlineData("REVIEW-TEAM")]
    public void CreateDocument_covers_all_platform_modules(string sku)
    {
        var document = LicenceSkuCatalog.CreateDocument(sku, Guid.NewGuid());
        Assert.Equal(PlatformModules.All.Count, document.Modules.Count);
        foreach (var module in PlatformModules.All)
        {
            Assert.True(document.Modules.ContainsKey(module));
        }
    }
}

public sealed class PlatformModulesNormalizeTests
{
    [Theory]
    [InlineData("pipelines", "build")]
    [InlineData("PIPELINES", "build")]
    [InlineData("build", "build")]
    [InlineData("build-team", "build")]
    [InlineData("build-enterprise", "build")]
    [InlineData("review", "review")]
    [InlineData("review-team", "review")]
    [InlineData("review-enterprise", "review")]
    [InlineData("review-commercial", "review")]
    [InlineData("deploy", "deploy")]
    [InlineData("deploy-team", "deploy")]
    [InlineData("deploy-enterprise", "deploy")]
    [InlineData("git", "git")]
    [InlineData("git-team", "git")]
    [InlineData("code", "code")]
    [InlineData("code-enterprise", "code")]
    public void Normalize_maps_runtime_ids(string input, string expected) =>
        Assert.Equal(expected, PlatformModules.Normalize(input));
}

public sealed class DeploymentTopologyTests
{
    [Theory]
    [InlineData(null, "appliance")]
    [InlineData("", "appliance")]
    [InlineData("  ", "appliance")]
    [InlineData("appliance", "appliance")]
    [InlineData("APPLIANCE", "appliance")]
    [InlineData("team-split", "team-split")]
    [InlineData("TEAM-SPLIT", "team-split")]
    [InlineData("enterprise", "enterprise")]
    [InlineData("ENTERPRISE", "enterprise")]
    [InlineData("unknown", "appliance")]
    [InlineData("microservices", "appliance")]
    public void Profile_resolver_normalizes(string? input, string expected) =>
        Assert.Equal(expected, DeploymentProfileResolver.Resolve(input));

    [Theory]
    [InlineData(DeploymentDomains.PlatformCore)]
    [InlineData(DeploymentDomains.Scm)]
    [InlineData(DeploymentDomains.Build)]
    [InlineData(DeploymentDomains.Deploy)]
    public void Domains_are_non_empty(string domain) => Assert.False(string.IsNullOrWhiteSpace(domain));

    [Theory]
    [InlineData(ModuleDatabases.ConnectionKeys.Platform)]
    [InlineData(ModuleDatabases.ConnectionKeys.Git)]
    [InlineData(ModuleDatabases.ConnectionKeys.Review)]
    [InlineData(ModuleDatabases.ConnectionKeys.Build)]
    [InlineData(ModuleDatabases.ConnectionKeys.Deploy)]
    public void Connection_keys_are_aspnet_style(string key) =>
        Assert.Matches("^[A-Z][a-zA-Z]+$", key);

    [Fact]
    public void Soft_limits_match_feature_matrix()
    {
        Assert.Equal(1, CommunityLimits.ReviewMaxApprovalRules);
        Assert.Equal(1, CommunityLimits.BuildMaxConcurrentPipelines);
        Assert.Equal(1, CommunityLimits.DeployMaxEnvironments);
    }
}

public sealed class OrganisationDeployPermissionTests
{
    public static IEnumerable<object[]> RolesAndDeployPerms()
    {
        foreach (var role in Enum.GetValues<ForgeDeck.Core.Domain.OrganisationRole>())
        {
            yield return [role, "deploy.read", true];
            yield return [role, "deploy.execute", true];
            yield return [role, "deploy.manage", true];
            yield return [role, "pipelines.read", true];
            yield return [role, "pipelines.run", true];
        }
    }

    [Theory]
    [MemberData(nameof(RolesAndDeployPerms))]
    public void All_roles_include_module_deploy_and_build_permissions(
        ForgeDeck.Core.Domain.OrganisationRole role, string permission, bool expected)
    {
        var set = ForgeDeck.Core.Identity.OrganisationPermissions.ForRole(role);
        Assert.Equal(expected, set.Contains(permission));
    }
}
