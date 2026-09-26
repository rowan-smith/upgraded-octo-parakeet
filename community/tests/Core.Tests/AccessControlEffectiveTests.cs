using ForgeDeck.Core.Application;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Identity;

namespace Core.Tests;

/// <summary>
/// Builds every (organisation role x system access role) combination once against a single SQLite tenancy so the
/// matrix theories below are dictionary lookups rather than one database per case.
/// </summary>
public sealed class EffectivePermissionMatrixFixture : IDisposable
{
    private readonly TenancyFixture _tenancy = new();
    private readonly Dictionary<string, IReadOnlySet<string>> _teamGrants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlySet<string>> _userGrants = new(StringComparer.Ordinal);

    public EffectivePermissionMatrixFixture()
    {
        var owner = _tenancy.Bootstrap("matrix-owner@forgedeck.dev", "matrixowner", "Matrix Owner");
        var roles = new RoleService(_tenancy.Store);
        roles.EnsureSystemRoles();
        var effective = new EffectivePermissionService(_tenancy.Store, roles, _tenancy.Access);

        foreach (var orgRole in Enum.GetValues<OrganisationRole>())
        {
            foreach (var slug in SystemAccessRoles.Slugs)
            {
                var role = roles.FindBySlug(slug)!;
                var suffix = $"{orgRole}-{slug}".ToLowerInvariant();
                var key = Key(orgRole, slug);

                var viaTeam = AddMember($"team-{suffix}", orgRole, owner.Id);
                var teamProject = CreateProject($"team-{suffix}", owner.Id);
                var team = _tenancy.Teams.Create($"Team {suffix}", $"team-{suffix}", null);
                _tenancy.Teams.AddMember(team.Id, viaTeam.Id);
                roles.AssignTeamRole(team.Id, role.Id);
                _tenancy.Projects.GrantTeam(teamProject.Id, team.Id);
                _teamGrants[key] = effective.ForUser(viaTeam.Id, teamProject.Id);

                var direct = AddMember($"user-{suffix}", orgRole, owner.Id);
                var directProject = CreateProject($"user-{suffix}", owner.Id);
                _tenancy.Projects.GrantUser(directProject.Id, direct.Id, role.Id);
                _userGrants[key] = effective.ForUser(direct.Id, directProject.Id);
            }
        }
    }

    public IReadOnlySet<string> ViaTeamRole(OrganisationRole orgRole, string slug) => _teamGrants[Key(orgRole, slug)];

    public IReadOnlySet<string> ViaUserGrant(OrganisationRole orgRole, string slug) => _userGrants[Key(orgRole, slug)];

    public void Dispose() => _tenancy.Dispose();

    private static string Key(OrganisationRole orgRole, string slug) => $"{orgRole}|{slug}";

    private UserAccount AddMember(string handle, OrganisationRole orgRole, Guid invitedBy) =>
        _tenancy.Memberships.Add($"{handle}@forgedeck.dev", handle, handle, "password123", orgRole, invitedBy);

    private Project CreateProject(string slug, Guid ownerId) =>
        _tenancy.Projects.CreateProject(new CreateProjectRequest($"Project {slug}", slug, null, null), ownerId);
}

/// <summary>
/// The organisation membership role is the ceiling; a role attached to a project or team grant narrows it. These
/// theories assert the intersection for every role pair and every permission in the catalogue.
/// </summary>
public sealed class AccessControlEffectiveTests(EffectivePermissionMatrixFixture matrix)
    : IClassFixture<EffectivePermissionMatrixFixture>
{
    public static TheoryData<OrganisationRole, string, string> RoleGrantPermissionMatrix()
    {
        var data = new TheoryData<OrganisationRole, string, string>();
        foreach (var orgRole in Enum.GetValues<OrganisationRole>())
        {
            foreach (var slug in SystemAccessRoles.Slugs)
            {
                foreach (var permission in PermissionCatalogue.AllKeys.OrderBy(key => key, StringComparer.Ordinal))
                {
                    data.Add(orgRole, slug, permission);
                }
            }
        }

        return data;
    }

    public static TheoryData<OrganisationRole, string> RoleGrantPairs()
    {
        var data = new TheoryData<OrganisationRole, string>();
        foreach (var orgRole in Enum.GetValues<OrganisationRole>())
        {
            foreach (var slug in SystemAccessRoles.Slugs)
            {
                data.Add(orgRole, slug);
            }
        }

        return data;
    }

    public static TheoryData<OrganisationRole> OrganisationRoles()
    {
        var data = new TheoryData<OrganisationRole>();
        foreach (var orgRole in Enum.GetValues<OrganisationRole>())
        {
            data.Add(orgRole);
        }

        return data;
    }

    public static TheoryData<string> SystemRoleSlugs()
    {
        var data = new TheoryData<string>();
        foreach (var slug in SystemAccessRoles.Slugs)
        {
            data.Add(slug);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RoleGrantPermissionMatrix))]
    public void Team_role_grant_intersects_the_organisation_ceiling(OrganisationRole orgRole, string slug, string permission)
    {
        var expected = Expected(orgRole, slug).Contains(permission);

        Assert.Equal(expected, matrix.ViaTeamRole(orgRole, slug).Contains(permission));
    }

    [Theory]
    [MemberData(nameof(RoleGrantPermissionMatrix))]
    public void Direct_project_role_grant_intersects_the_organisation_ceiling(OrganisationRole orgRole, string slug, string permission)
    {
        var expected = Expected(orgRole, slug).Contains(permission);

        Assert.Equal(expected, matrix.ViaUserGrant(orgRole, slug).Contains(permission));
    }

    [Theory]
    [MemberData(nameof(RoleGrantPairs))]
    public void Team_and_direct_grants_of_the_same_role_agree(OrganisationRole orgRole, string slug) =>
        Assert.Equal(
            matrix.ViaUserGrant(orgRole, slug).OrderBy(key => key, StringComparer.Ordinal),
            matrix.ViaTeamRole(orgRole, slug).OrderBy(key => key, StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(RoleGrantPairs))]
    public void Granted_permissions_never_exceed_the_ceiling(OrganisationRole orgRole, string slug)
    {
        var ceiling = OrganisationPermissions.ForRole(orgRole);

        Assert.All(matrix.ViaTeamRole(orgRole, slug), permission => Assert.Contains(permission, ceiling));
    }

    [Theory]
    [MemberData(nameof(RoleGrantPairs))]
    public void Granted_permissions_are_all_known_catalogue_keys(OrganisationRole orgRole, string slug) =>
        Assert.All(matrix.ViaTeamRole(orgRole, slug), permission => Assert.True(PermissionCatalogue.IsKnown(permission)));

    [Theory]
    [MemberData(nameof(OrganisationRoles))]
    public void Owner_role_grant_leaves_the_ceiling_untouched_for_that_role(OrganisationRole orgRole)
    {
        var ceiling = OrganisationPermissions.ForRole(orgRole);
        var effective = matrix.ViaTeamRole(orgRole, SystemAccessRoles.SlugFor(orgRole));

        Assert.Equal(ceiling.OrderBy(key => key, StringComparer.Ordinal), effective.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(OrganisationRoles))]
    public void Reader_grant_removes_every_write_permission(OrganisationRole orgRole)
    {
        var effective = matrix.ViaTeamRole(orgRole, SystemAccessRoles.Reader);

        Assert.Contains("review.read", effective);
        Assert.Contains("pipelines.read", effective);
        Assert.Contains("deploy.read", effective);
        Assert.DoesNotContain("review.merge", effective);
        Assert.DoesNotContain("pipelines.manage", effective);
        Assert.DoesNotContain("deploy.execute", effective);
        Assert.DoesNotContain(OrganisationPermissions.UsersManage, effective);
        Assert.DoesNotContain(OrganisationPermissions.OrganisationDestroy, effective);
    }

    [Theory]
    [MemberData(nameof(OrganisationRoles))]
    public void Builder_grant_keeps_pipelines_and_drops_deploy_writes(OrganisationRole orgRole)
    {
        var effective = matrix.ViaTeamRole(orgRole, SystemAccessRoles.Builder);

        Assert.Contains("pipelines.manage", effective);
        Assert.Contains("pipelines.run", effective);
        Assert.Contains("pipelines.cancel", effective);
        Assert.DoesNotContain("deploy.execute", effective);
        Assert.DoesNotContain("review.merge", effective);
    }

    [Theory]
    [MemberData(nameof(OrganisationRoles))]
    public void Deployer_grant_keeps_deploy_and_drops_pipeline_writes(OrganisationRole orgRole)
    {
        var effective = matrix.ViaTeamRole(orgRole, SystemAccessRoles.Deployer);

        Assert.Contains("deploy.execute", effective);
        Assert.Contains("deploy.manage", effective);
        Assert.DoesNotContain("pipelines.manage", effective);
        Assert.DoesNotContain("pipelines.run", effective);
    }

    [Theory]
    [MemberData(nameof(OrganisationRoles))]
    public void Reviewer_grant_keeps_approval_and_drops_merge(OrganisationRole orgRole)
    {
        var effective = matrix.ViaTeamRole(orgRole, SystemAccessRoles.Reviewer);

        Assert.Contains("review.approve", effective);
        Assert.Contains("review.comment", effective);
        Assert.Contains("review.request", effective);
        Assert.DoesNotContain("review.merge", effective);
        Assert.DoesNotContain("review.manage", effective);
    }

    [Theory]
    [MemberData(nameof(OrganisationRoles))]
    public void Developer_grant_keeps_merge_and_drops_deploy_execution(OrganisationRole orgRole)
    {
        var effective = matrix.ViaTeamRole(orgRole, SystemAccessRoles.Developer);

        Assert.Contains("review.merge", effective);
        Assert.Contains("pipelines.run", effective);
        Assert.DoesNotContain("deploy.execute", effective);
        Assert.DoesNotContain("deploy.manage", effective);
    }

    [Theory]
    [MemberData(nameof(SystemRoleSlugs))]
    public void Every_system_role_narrows_a_member_to_at_most_the_member_ceiling(string slug)
    {
        var ceiling = OrganisationPermissions.ForRole(OrganisationRole.Member);

        Assert.All(matrix.ViaTeamRole(OrganisationRole.Member, slug), permission => Assert.Contains(permission, ceiling));
    }

    [Theory]
    [MemberData(nameof(SystemRoleSlugs))]
    public void Owner_grant_of_any_role_never_gains_permissions_the_role_lacks(string slug)
    {
        var rolePermissions = SystemAccessRoles.PermissionsFor(slug);

        Assert.All(
            matrix.ViaTeamRole(OrganisationRole.Owner, slug),
            permission => Assert.Contains(permission, rolePermissions));
    }

    private static IReadOnlySet<string> Expected(OrganisationRole orgRole, string slug) =>
        EffectivePermissionService.Intersect(
            OrganisationPermissions.ForRole(orgRole),
            SystemAccessRoles.PermissionsFor(slug));
}

/// <summary>Effective-permission behaviour that needs a bespoke tenancy shape per case.</summary>
public sealed class AccessControlEffectiveEdgeTests
{
    [Fact]
    public void No_membership_yields_no_permissions()
    {
        using var world = new World();

        Assert.Empty(world.Effective.ForUser(Guid.NewGuid()));
    }

    [Fact]
    public void Suspended_membership_yields_no_permissions()
    {
        using var world = new World();
        var member = world.AddMember("suspended", OrganisationRole.Member);
        world.Tenancy.Memberships.ChangeStatus(member.Id, MembershipStatus.Suspended);

        Assert.Empty(world.Effective.ForUser(member.Id));
    }

    [Theory]
    [InlineData(OrganisationRole.Owner)]
    [InlineData(OrganisationRole.Admin)]
    [InlineData(OrganisationRole.Member)]
    public void Without_a_project_the_ceiling_applies_verbatim(OrganisationRole orgRole)
    {
        using var world = new World();
        var member = world.AddMember($"ceiling-{orgRole}".ToLowerInvariant(), orgRole);

        Assert.Equal(
            OrganisationPermissions.ForRole(orgRole).OrderBy(key => key, StringComparer.Ordinal),
            world.Effective.ForUser(member.Id).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void Unknown_project_falls_back_to_the_ceiling()
    {
        using var world = new World();
        var member = world.AddMember("unknown-project", OrganisationRole.Member);

        Assert.Equal(
            OrganisationPermissions.ForRole(OrganisationRole.Member).OrderBy(key => key, StringComparer.Ordinal),
            world.Effective.ForUser(member.Id, Guid.NewGuid()).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void Grant_without_a_role_does_not_narrow_anything()
    {
        using var world = new World();
        var member = world.AddMember("no-role-grant", OrganisationRole.Member);
        var project = world.CreateProject("no-role-grant");
        world.Tenancy.Projects.GrantUser(project.Id, member.Id);

        Assert.Equal(
            OrganisationPermissions.ForRole(OrganisationRole.Member).OrderBy(key => key, StringComparer.Ordinal),
            world.Effective.ForUser(member.Id, project.Id).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void Team_membership_without_a_project_grant_does_not_narrow_anything()
    {
        using var world = new World();
        var member = world.AddMember("ungranted-team", OrganisationRole.Member);
        var project = world.CreateProject("ungranted-team", ProjectVisibility.Organisation);
        var team = world.Tenancy.Teams.Create("Ungranted", "ungranted", null);
        world.Tenancy.Teams.AddMember(team.Id, member.Id);
        world.Roles.AssignTeamRole(team.Id, world.Role(SystemAccessRoles.Reader).Id);

        Assert.Contains("review.merge", world.Effective.ForUser(member.Id, project.Id));
    }

    [Fact]
    public void Inaccessible_private_project_falls_back_to_the_ceiling()
    {
        using var world = new World();
        var member = world.AddMember("outsider", OrganisationRole.Member);
        var project = world.CreateProject("locked");
        var team = world.Tenancy.Teams.Create("Locked readers", "locked-readers", null);
        world.Roles.AssignTeamRole(team.Id, world.Role(SystemAccessRoles.Reader).Id);
        world.Tenancy.Projects.GrantTeam(project.Id, team.Id);

        // The user is in no granted team, so the reader role on the grant must not apply to them.
        Assert.Contains("review.merge", world.Effective.ForUser(member.Id, project.Id));
    }

    [Fact]
    public void Two_team_roles_union_before_intersecting_the_ceiling()
    {
        using var world = new World();
        var member = world.AddMember("two-teams", OrganisationRole.Member);
        var project = world.CreateProject("two-teams");

        var builders = world.Tenancy.Teams.Create("Builders", "builders", null);
        world.Roles.AssignTeamRole(builders.Id, world.Role(SystemAccessRoles.Builder).Id);
        world.Tenancy.Teams.AddMember(builders.Id, member.Id);
        world.Tenancy.Projects.GrantTeam(project.Id, builders.Id);

        var deployers = world.Tenancy.Teams.Create("Deployers", "deployers", null);
        world.Roles.AssignTeamRole(deployers.Id, world.Role(SystemAccessRoles.Deployer).Id);
        world.Tenancy.Teams.AddMember(deployers.Id, member.Id);
        world.Tenancy.Projects.GrantTeam(project.Id, deployers.Id);

        var effective = world.Effective.ForUser(member.Id, project.Id);

        Assert.Contains("pipelines.manage", effective);
        Assert.Contains("deploy.execute", effective);
        Assert.DoesNotContain("review.merge", effective);
    }

    [Fact]
    public void Grant_role_overrides_the_role_attached_to_the_team()
    {
        using var world = new World();
        var member = world.AddMember("grant-wins", OrganisationRole.Member);
        var project = world.CreateProject("grant-wins");
        var team = world.Tenancy.Teams.Create("Mixed", "mixed", null);
        world.Roles.AssignTeamRole(team.Id, world.Role(SystemAccessRoles.Reader).Id);
        world.Tenancy.Teams.AddMember(team.Id, member.Id);
        world.Tenancy.Projects.GrantTeam(project.Id, team.Id, world.Role(SystemAccessRoles.Builder).Id);

        var effective = world.Effective.ForUser(member.Id, project.Id);

        Assert.Contains("pipelines.manage", effective);
    }

    [Fact]
    public void Direct_and_team_grants_union_for_the_same_project()
    {
        using var world = new World();
        var member = world.AddMember("both-grants", OrganisationRole.Member);
        var project = world.CreateProject("both-grants");
        world.Tenancy.Projects.GrantUser(project.Id, member.Id, world.Role(SystemAccessRoles.Reviewer).Id);
        var team = world.Tenancy.Teams.Create("Both builders", "both-builders", null);
        world.Roles.AssignTeamRole(team.Id, world.Role(SystemAccessRoles.Builder).Id);
        world.Tenancy.Teams.AddMember(team.Id, member.Id);
        world.Tenancy.Projects.GrantTeam(project.Id, team.Id);

        var effective = world.Effective.ForUser(member.Id, project.Id);

        Assert.Contains("review.approve", effective);
        Assert.Contains("pipelines.manage", effective);
        Assert.DoesNotContain("deploy.execute", effective);
    }

    [Fact]
    public void Custom_role_grant_narrows_to_exactly_its_permissions()
    {
        using var world = new World();
        var member = world.AddMember("custom-role", OrganisationRole.Member);
        var project = world.CreateProject("custom-role");
        var custom = world.Roles.Create("Pipeline watcher", "pipeline-watcher", ["pipelines.read", "review.read"], "Narrow");
        world.Tenancy.Projects.GrantUser(project.Id, member.Id, custom.Id);

        var effective = world.Effective.ForUser(member.Id, project.Id);

        Assert.Equal(["pipelines.read", "review.read"], effective.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void Custom_role_cannot_grant_beyond_the_member_ceiling()
    {
        using var world = new World();
        var member = world.AddMember("escalate", OrganisationRole.Member);
        var project = world.CreateProject("escalate");
        var custom = world.Roles.Create(
            "Escalator",
            "escalator",
            [OrganisationPermissions.OrganisationDestroy, OrganisationPermissions.UsersManage, "pipelines.read"],
            null);
        world.Tenancy.Projects.GrantUser(project.Id, member.Id, custom.Id);

        var effective = world.Effective.ForUser(member.Id, project.Id);

        Assert.Equal(["pipelines.read"], effective);
    }

    [Theory]
    [InlineData("pipelines.manage", true)]
    [InlineData("pipelines.read", true)]
    [InlineData("deploy.execute", false)]
    [InlineData("review.merge", false)]
    [InlineData("organisation.destroy", false)]
    public void Has_mirrors_ForUser(string permission, bool expected)
    {
        using var world = new World();
        var member = world.AddMember("has-check", OrganisationRole.Member);
        var project = world.CreateProject("has-check");
        world.Tenancy.Projects.GrantUser(project.Id, member.Id, world.Role(SystemAccessRoles.Builder).Id);

        Assert.Equal(expected, world.Effective.Has(member.Id, permission, project.Id));
    }

    [Fact]
    public void Deleting_a_role_releases_the_grant_and_restores_the_ceiling()
    {
        using var world = new World();
        var member = world.AddMember("released", OrganisationRole.Member);
        var project = world.CreateProject("released");
        var custom = world.Roles.Create("Temporary", "temporary", ["pipelines.read"], null);
        world.Tenancy.Projects.GrantUser(project.Id, member.Id, custom.Id);
        Assert.Equal(["pipelines.read"], world.Effective.ForUser(member.Id, project.Id));

        world.Roles.Delete(custom.Id);

        Assert.Contains("review.merge", world.Effective.ForUser(member.Id, project.Id));
    }

    [Fact]
    public void Intersect_is_case_insensitive_and_drops_unmatched_keys()
    {
        var ceiling = OrganisationPermissions.ForRole(OrganisationRole.Member);

        var result = EffectivePermissionService.Intersect(ceiling, ["PIPELINES.READ", "organisation.destroy", "nope"]);

        Assert.Contains("pipelines.read", result);
        Assert.DoesNotContain("organisation.destroy", result);
        Assert.DoesNotContain("nope", result);
    }

    [Fact]
    public void Intersect_of_an_empty_grant_is_empty() =>
        Assert.Empty(EffectivePermissionService.Intersect(OrganisationPermissions.ForRole(OrganisationRole.Owner), []));

    private sealed class World : IDisposable
    {
        public World()
        {
            Tenancy = new TenancyFixture();
            Owner = Tenancy.Bootstrap("edge-owner@forgedeck.dev", "edgeowner", "Edge Owner");
            Roles = new RoleService(Tenancy.Store);
            Roles.EnsureSystemRoles();
            Effective = new EffectivePermissionService(Tenancy.Store, Roles, Tenancy.Access);
        }

        public TenancyFixture Tenancy { get; }
        public UserAccount Owner { get; }
        public RoleService Roles { get; }
        public EffectivePermissionService Effective { get; }

        public AccessRole Role(string slug) => Roles.FindBySlug(slug)!;

        public UserAccount AddMember(string handle, OrganisationRole orgRole) =>
            Tenancy.Memberships.Add($"{handle}@forgedeck.dev", handle, handle, "password123", orgRole, Owner.Id);

        public Project CreateProject(string slug, ProjectVisibility visibility = ProjectVisibility.Private) =>
            Tenancy.Projects.CreateProject(
                new CreateProjectRequest($"Project {slug}", slug, null, null, visibility),
                Owner.Id);

        public void Dispose() => Tenancy.Dispose();
    }
}
