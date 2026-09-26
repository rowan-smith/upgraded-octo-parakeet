using ForgeDeck.Core.Application;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Identity;

namespace Core.Tests;

/// <summary>
/// Builds every (organisation role × system access role) combination once against a single SQLite tenancy so the
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
        var effective = new EffectivePermissionService(_tenancy.Store, roles, new PermissionDefinitionRegistry(), _tenancy.Access);

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
                _tenancy.Teams.AddMember(team.Id, viaTeam.Id, assignDefaultTeamRole: false);
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
/// Effective permissions are the additive union of organisation role permissions and applicable project/team grants.
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
    public void Team_role_grant_unions_with_organisation_role(OrganisationRole orgRole, string slug, string permission)
    {
        var expected = Expected(orgRole, slug).Contains(permission);
        Assert.Equal(expected, matrix.ViaTeamRole(orgRole, slug).Contains(permission));
    }

    [Theory]
    [MemberData(nameof(RoleGrantPermissionMatrix))]
    public void Direct_project_role_grant_unions_with_organisation_role(OrganisationRole orgRole, string slug, string permission)
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
    public void Granted_permissions_are_all_known_catalogue_keys(OrganisationRole orgRole, string slug) =>
        Assert.All(matrix.ViaTeamRole(orgRole, slug), permission => Assert.True(PermissionCatalogue.IsKnown(permission)));

    [Theory]
    [MemberData(nameof(OrganisationRoles))]
    public void Member_with_reader_gains_read_module_permissions(OrganisationRole orgRole)
    {
        if (orgRole is not OrganisationRole.Member)
        {
            return;
        }

        var effective = matrix.ViaTeamRole(OrganisationRole.Member, SystemAccessRoles.Reader);
        Assert.Contains("review.read", effective);
        Assert.Contains("pipelines.read", effective);
        Assert.DoesNotContain("review.merge", effective);
        Assert.DoesNotContain("deploy.execute", effective);
        Assert.DoesNotContain(OrganisationPermissions.UsersManage, effective);
    }

    [Theory]
    [MemberData(nameof(OrganisationRoles))]
    public void Member_with_builder_gains_pipeline_writes(OrganisationRole orgRole)
    {
        if (orgRole is not OrganisationRole.Member)
        {
            return;
        }

        var effective = matrix.ViaTeamRole(OrganisationRole.Member, SystemAccessRoles.Builder);
        Assert.Contains("pipelines.manage", effective);
        Assert.Contains("pipelines.run", effective);
        Assert.DoesNotContain("deploy.execute", effective);
        Assert.DoesNotContain("review.merge", effective);
    }

    [Theory]
    [MemberData(nameof(OrganisationRoles))]
    public void Member_with_reviewer_can_approve_but_not_merge(OrganisationRole orgRole)
    {
        if (orgRole is not OrganisationRole.Member)
        {
            return;
        }

        var effective = matrix.ViaTeamRole(OrganisationRole.Member, SystemAccessRoles.Reviewer);
        Assert.Contains("review.approve", effective);
        Assert.DoesNotContain("review.merge", effective);
    }

    [Theory]
    [MemberData(nameof(SystemRoleSlugs))]
    public void Owner_always_retains_organisation_owner_permissions(string slug)
    {
        var ownerPerms = OrganisationPermissions.ForRole(OrganisationRole.Owner);
        Assert.All(ownerPerms, permission => Assert.Contains(permission, matrix.ViaTeamRole(OrganisationRole.Owner, slug)));
    }

    private static IReadOnlySet<string> Expected(OrganisationRole orgRole, string slug)
    {
        var result = new HashSet<string>(OrganisationPermissions.ForRole(orgRole), StringComparer.OrdinalIgnoreCase);
        foreach (var permission in SystemAccessRoles.PermissionsFor(slug))
        {
            if (PermissionCatalogue.AllowsScope(permission, ScopeType.Project))
            {
                result.Add(permission);
            }
        }

        return result;
    }
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
    public void Without_a_project_the_organisation_role_applies_verbatim(OrganisationRole orgRole)
    {
        using var world = new World();
        var member = world.AddMember($"org-{orgRole}".ToLowerInvariant(), orgRole);
        Assert.Equal(
            OrganisationPermissions.ForRole(orgRole).OrderBy(key => key, StringComparer.Ordinal),
            world.Effective.ForUser(member.Id).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void Grant_without_a_role_adds_no_project_permissions()
    {
        using var world = new World();
        var member = world.AddMember("plain", OrganisationRole.Member);
        var project = world.CreateProject("plain");
        world.Tenancy.Projects.GrantUser(project.Id, member.Id);

        Assert.Equal(
            OrganisationPermissions.ForRole(OrganisationRole.Member).OrderBy(p => p),
            world.Effective.ForUser(member.Id, project.Id).OrderBy(p => p));
        Assert.DoesNotContain("review.read", world.Effective.ForUser(member.Id, project.Id));
    }

    [Fact]
    public void Two_team_roles_union_additively()
    {
        using var world = new World();
        var member = world.AddMember("union", OrganisationRole.Member);
        var project = world.CreateProject("union");
        var reviewers = world.Tenancy.Teams.Create("Reviewers", "reviewers", null);
        var builders = world.Tenancy.Teams.Create("Builders", "builders", null);
        world.Tenancy.Teams.AddMember(reviewers.Id, member.Id);
        world.Tenancy.Teams.AddMember(builders.Id, member.Id);
        world.Tenancy.Projects.GrantTeam(project.Id, reviewers.Id, world.Tenancy.SystemRole(SystemAccessRoles.Reviewer).Id);
        world.Tenancy.Projects.GrantTeam(project.Id, builders.Id, world.Tenancy.SystemRole(SystemAccessRoles.Builder).Id);

        var effective = world.Effective.ForUser(member.Id, project.Id);
        Assert.Contains("review.approve", effective);
        Assert.Contains("pipelines.run", effective);
        Assert.DoesNotContain("review.merge", effective);
    }

    [Fact]
    public void Custom_role_grant_adds_only_project_scoped_permissions()
    {
        using var world = new World();
        var member = world.AddMember("custom", OrganisationRole.Member);
        var project = world.CreateProject("custom");
        var role = world.Tenancy.Roles.Create("Limited", "limited", ["review.read", "organisation.destroy"]);
        world.Tenancy.Projects.GrantUser(project.Id, member.Id, role.Id);

        var effective = world.Effective.ForUser(member.Id, project.Id);
        Assert.Contains("review.read", effective);
        Assert.DoesNotContain("organisation.destroy", effective);
    }

    [Fact]
    public void Deleting_a_role_releases_the_grant()
    {
        using var world = new World();
        var member = world.AddMember("delete-role", OrganisationRole.Member);
        var project = world.CreateProject("delete-role");
        var role = world.Tenancy.Roles.Create("Temp", "temp-role", ["review.read"]);
        world.Tenancy.Projects.GrantUser(project.Id, member.Id, role.Id);
        Assert.Contains("review.read", world.Effective.ForUser(member.Id, project.Id));

        world.Tenancy.Roles.Delete(role.Id);
        Assert.DoesNotContain("review.read", world.Effective.ForUser(member.Id, project.Id));
    }

    [Theory]
    [InlineData("review.read", true)]
    [InlineData("REVIEW.READ", true)]
    [InlineData("review.merge", false)]
    public void Permission_checks_are_case_insensitive(string permission, bool expected)
    {
        using var world = new World();
        var member = world.AddMember("case", OrganisationRole.Member);
        var project = world.CreateProject("case");
        world.Tenancy.Projects.GrantUser(project.Id, member.Id, world.Tenancy.SystemRole(SystemAccessRoles.Reader).Id);
        Assert.Equal(expected, world.Effective.Has(member.Id, permission, project.Id));
    }

    private sealed class World : IDisposable
    {
        public World()
        {
            Tenancy = new TenancyFixture();
            Owner = Tenancy.Bootstrap("edge-owner@forgedeck.dev", "edgeowner", "Edge Owner");
            Effective = Tenancy.Effective;
        }

        public TenancyFixture Tenancy { get; }
        public UserAccount Owner { get; }
        public EffectivePermissionService Effective { get; }

        public UserAccount AddMember(string handle, OrganisationRole role) =>
            Tenancy.Memberships.Add($"{handle}@forgedeck.dev", handle, handle, "password123", role, Owner.Id);

        public Project CreateProject(string slug) =>
            Tenancy.Projects.CreateProject(new CreateProjectRequest($"Project {slug}", slug, null, null), Owner.Id);

        public void Dispose() => Tenancy.Dispose();
    }
}
