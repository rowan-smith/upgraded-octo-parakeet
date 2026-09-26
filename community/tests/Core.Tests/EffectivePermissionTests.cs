using ForgeDeck.Core.Application;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Identity;

namespace Core.Tests;

/// <summary>
/// One organisation wired up with every combination the resolver has to handle, shared by the permission
/// matrices below so hundreds of cases do not each pay for a database and a password hash.
/// </summary>
public sealed class EffectivePermissionScenario : IDisposable
{
    private readonly TenancyFixture _fixture = new();
    private readonly Dictionary<string, Guid> _teamGrantUsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _directGrantUsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _adminUsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _teamRoleOnlyUsers = new(StringComparer.OrdinalIgnoreCase);

    public EffectivePermissionScenario()
    {
        Owner = _fixture.Bootstrap().Id;
        Private = _fixture.Projects.CreateProject(
            new CreateProjectRequest("Atlas", "atlas", null, null, ProjectVisibility.Private), Owner).Id;
        Shared = _fixture.Projects.CreateProject(
            new CreateProjectRequest("Shared", "shared", null, null, ProjectVisibility.Organisation), Owner).Id;

        foreach (var slug in SystemAccessRoles.Slugs)
        {
            var role = _fixture.SystemRole(slug).Id;

            // Member reaching the project through a team that carries the role on the grant.
            var viaTeamGrant = AddMember($"team-{slug}", OrganisationRole.Member);
            var grantTeam = _fixture.Teams.Create($"Grant {slug}", $"grant-{slug}", null);
            _fixture.Teams.AddMember(grantTeam.Id, viaTeamGrant);
            _fixture.Projects.GrantTeam(Private, grantTeam.Id, role);
            _teamGrantUsers[slug] = viaTeamGrant;

            // Member reaching the project through a direct grant that carries the role.
            var viaDirectGrant = AddMember($"direct-{slug}", OrganisationRole.Member);
            _fixture.Projects.GrantUser(Private, viaDirectGrant, role);
            _directGrantUsers[slug] = viaDirectGrant;

            // Admin ceiling narrowed by a role on a team grant.
            var admin = AddMember($"admin-{slug}", OrganisationRole.Admin);
            var adminTeam = _fixture.Teams.Create($"Admin {slug}", $"admin-team-{slug}", null);
            _fixture.Teams.AddMember(adminTeam.Id, admin);
            _fixture.Projects.GrantTeam(Private, adminTeam.Id, role);
            _adminUsers[slug] = admin;

            // Member whose team carries the role but whose grant does not: the group role applies.
            var viaTeamRole = AddMember($"group-{slug}", OrganisationRole.Member);
            var roleTeam = _fixture.Teams.Create($"Group {slug}", $"group-team-{slug}", null, role);
            _fixture.Teams.AddMember(roleTeam.Id, viaTeamRole);
            _fixture.Projects.GrantTeam(Private, roleTeam.Id);
            _teamRoleOnlyUsers[slug] = viaTeamRole;
        }

        RoleFreeGrantMember = AddMember("plain", OrganisationRole.Member);
        _fixture.Projects.GrantUser(Private, RoleFreeGrantMember);

        UngrantedMember = AddMember("outsider", OrganisationRole.Member);

        SuspendedMember = AddMember("suspended", OrganisationRole.Member);
        _fixture.Projects.GrantUser(Private, SuspendedMember);
        _fixture.Memberships.ChangeStatus(SuspendedMember, MembershipStatus.Suspended);

        var readerRole = _fixture.SystemRole(SystemAccessRoles.Reader).Id;
        var ownerTeam = _fixture.Teams.Create("Owner Readers", "owner-readers", null, readerRole);
        _fixture.Teams.AddMember(ownerTeam.Id, Owner);
        _fixture.Projects.GrantTeam(Private, ownerTeam.Id);

        // Two roles on one user: a direct Reviewer grant plus a Builder team grant.
        UnionMember = AddMember("union", OrganisationRole.Member);
        _fixture.Projects.GrantUser(Private, UnionMember, _fixture.SystemRole(SystemAccessRoles.Reviewer).Id);
        var unionTeam = _fixture.Teams.Create("Union Builders", "union-builders", null);
        _fixture.Teams.AddMember(unionTeam.Id, UnionMember);
        _fixture.Projects.GrantTeam(Private, unionTeam.Id, _fixture.SystemRole(SystemAccessRoles.Builder).Id);

        // Grant role beats the role attached to the group.
        OverrideMember = AddMember("override", OrganisationRole.Member);
        var overrideTeam = _fixture.Teams.Create("Override", "override-team", null, _fixture.SystemRole(SystemAccessRoles.Deployer).Id);
        _fixture.Teams.AddMember(overrideTeam.Id, OverrideMember);
        _fixture.Projects.GrantTeam(Private, overrideTeam.Id, readerRole);

        EmptyRole = _fixture.Roles.Create("Nothing", "nothing", []).Id;
        EmptyRoleMember = AddMember("empty", OrganisationRole.Member);
        _fixture.Projects.GrantUser(Private, EmptyRoleMember, EmptyRole);
    }

    public Guid Owner { get; }
    public Guid Private { get; }
    public Guid Shared { get; }
    public Guid RoleFreeGrantMember { get; }
    public Guid UngrantedMember { get; }
    public Guid SuspendedMember { get; }
    public Guid UnionMember { get; }
    public Guid OverrideMember { get; }
    public Guid EmptyRole { get; }
    public Guid EmptyRoleMember { get; }

    public EffectivePermissionService Effective => _fixture.Effective;
    public ProjectAccessService Access => _fixture.Access;
    public Project PrivateProject => _fixture.Projects.Get(Private)!;

    public Guid TeamGrantUser(string slug) => _teamGrantUsers[slug];
    public Guid DirectGrantUser(string slug) => _directGrantUsers[slug];
    public Guid AdminUser(string slug) => _adminUsers[slug];
    public Guid TeamRoleOnlyUser(string slug) => _teamRoleOnlyUsers[slug];

    public void Dispose() => _fixture.Dispose();

    private Guid AddMember(string handle, OrganisationRole role) =>
        _fixture.Memberships.Add($"{handle}@example.com", handle, handle, "password123", role, Owner).Id;
}

public sealed class EffectivePermissionMatrixTests(EffectivePermissionScenario scenario) : IClassFixture<EffectivePermissionScenario>
{
    public static IEnumerable<object[]> MemberCeiling() => Pairs(AccessMatrix.Member);
    public static IEnumerable<object[]> AdminCeiling() => Pairs(AccessMatrix.Admin);

    private static IEnumerable<object[]> Pairs(string[] ceiling)
    {
        foreach (var slug in AccessMatrix.Roles.Keys)
        {
            var expected = AccessMatrix.Intersection(slug, ceiling);
            foreach (var permission in AccessMatrix.AllPermissions)
            {
                yield return [slug, permission, expected.Contains(permission)];
            }
        }
    }

    [Theory]
    [MemberData(nameof(MemberCeiling))]
    public void Member_with_a_role_on_a_team_grant_gets_role_intersected_with_member(string slug, string permission, bool expected) =>
        Assert.Equal(expected, scenario.Effective.Has(scenario.TeamGrantUser(slug), permission, scenario.Private));

    [Theory]
    [MemberData(nameof(MemberCeiling))]
    public void Member_with_a_role_on_a_direct_grant_gets_role_intersected_with_member(string slug, string permission, bool expected) =>
        Assert.Equal(expected, scenario.Effective.Has(scenario.DirectGrantUser(slug), permission, scenario.Private));

    [Theory]
    [MemberData(nameof(MemberCeiling))]
    public void Member_inherits_the_role_attached_to_their_group(string slug, string permission, bool expected) =>
        Assert.Equal(expected, scenario.Effective.Has(scenario.TeamRoleOnlyUser(slug), permission, scenario.Private));

    [Theory]
    [MemberData(nameof(AdminCeiling))]
    public void Admin_ceiling_is_narrowed_by_the_granted_role(string slug, string permission, bool expected) =>
        Assert.Equal(expected, scenario.Effective.Has(scenario.AdminUser(slug), permission, scenario.Private));

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Owner_in_a_reader_team_is_narrowed_to_reader_on_that_project(string permission)
    {
        var expected = AccessMatrix.Intersection(SystemAccessRoles.Reader, AccessMatrix.Owner).Contains(permission);
        Assert.Equal(expected, scenario.Effective.Has(scenario.Owner, permission, scenario.Private));
    }

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Owner_keeps_the_full_ceiling_without_a_project_context(string permission) =>
        Assert.Equal(
            AccessMatrix.Expected(SystemAccessRoles.Owner).Contains(permission),
            scenario.Effective.Has(scenario.Owner, permission));

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Role_free_grant_leaves_the_member_ceiling_untouched(string permission) =>
        Assert.Equal(
            AccessMatrix.Expected(SystemAccessRoles.Member).Contains(permission),
            scenario.Effective.Has(scenario.RoleFreeGrantMember, permission, scenario.Private));

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Organisation_visible_project_without_grants_uses_the_ceiling(string permission) =>
        Assert.Equal(
            AccessMatrix.Expected(SystemAccessRoles.Member).Contains(permission),
            scenario.Effective.Has(scenario.RoleFreeGrantMember, permission, scenario.Shared));

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Member_without_a_grant_keeps_the_ceiling_but_cannot_reach_the_project(string permission) =>
        Assert.Equal(
            AccessMatrix.Expected(SystemAccessRoles.Member).Contains(permission),
            scenario.Effective.Has(scenario.UngrantedMember, permission, scenario.Private));

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Suspended_member_holds_no_permissions(string permission)
    {
        Assert.False(scenario.Effective.Has(scenario.SuspendedMember, permission));
        Assert.False(scenario.Effective.Has(scenario.SuspendedMember, permission, scenario.Private));
    }

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Multiple_roles_union_before_intersecting_with_the_ceiling(string permission)
    {
        var union = new HashSet<string>(AccessMatrix.Roles[SystemAccessRoles.Reviewer], StringComparer.OrdinalIgnoreCase);
        union.UnionWith(AccessMatrix.Roles[SystemAccessRoles.Builder]);
        var expected = union.Contains(permission) && AccessMatrix.Expected(SystemAccessRoles.Member).Contains(permission);

        Assert.Equal(expected, scenario.Effective.Has(scenario.UnionMember, permission, scenario.Private));
    }

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Role_on_the_grant_overrides_the_role_on_the_group(string permission) =>
        Assert.Equal(
            AccessMatrix.Expected(SystemAccessRoles.Reader).Contains(permission),
            scenario.Effective.Has(scenario.OverrideMember, permission, scenario.Private));

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void An_empty_role_removes_every_permission_on_that_project(string permission)
    {
        Assert.False(scenario.Effective.Has(scenario.EmptyRoleMember, permission, scenario.Private));
        Assert.True(scenario.Effective.Has(scenario.EmptyRoleMember, permission) ==
                    AccessMatrix.Expected(SystemAccessRoles.Member).Contains(permission));
    }

    [Theory]
    [InlineData("ORGANISATION.READ")]
    [InlineData("Review.Read")]
    [InlineData("deploy.READ")]
    public void Effective_permission_checks_ignore_casing(string permission) =>
        Assert.True(scenario.Effective.Has(scenario.TeamGrantUser(SystemAccessRoles.Reader), permission, scenario.Private));

    [Fact]
    public void Unknown_project_context_falls_back_to_the_ceiling() =>
        Assert.Equal(
            AccessMatrix.Expected(SystemAccessRoles.Member).OrderBy(p => p),
            scenario.Effective.ForUser(scenario.RoleFreeGrantMember, Guid.NewGuid()).OrderBy(p => p));

    [Fact]
    public void Unknown_user_holds_no_permissions() =>
        Assert.Empty(scenario.Effective.ForUser(Guid.NewGuid(), scenario.Private));

    [Fact]
    public void Member_without_a_grant_cannot_access_the_private_project() =>
        Assert.False(scenario.Access.CanAccess(scenario.PrivateProject, scenario.UngrantedMember, OrganisationRole.Member));

    [Fact]
    public void Reader_grant_still_grants_project_access() =>
        Assert.True(scenario.Access.CanAccess(
            scenario.PrivateProject,
            scenario.DirectGrantUser(SystemAccessRoles.Reader),
            OrganisationRole.Member));
}

public sealed class EffectivePermissionResolutionTests
{
    [Fact]
    public void Removing_the_role_from_a_grant_restores_the_ceiling()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var reader = fixture.SystemRole(SystemAccessRoles.Reader);

        fixture.Projects.GrantUser(project.Id, bob.Id, reader.Id);
        Assert.False(fixture.Effective.Has(bob.Id, "review.merge", project.Id));

        fixture.Projects.GrantUser(project.Id, bob.Id);
        Assert.True(fixture.Effective.Has(bob.Id, "review.merge", project.Id));
    }

    [Fact]
    public void Deleting_a_custom_role_restores_the_ceiling()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var role = fixture.Roles.Create("Reader Only", "reader-only", ["review.read"]);

        fixture.Projects.GrantUser(project.Id, bob.Id, role.Id);
        Assert.False(fixture.Effective.Has(bob.Id, "review.merge", project.Id));

        fixture.Roles.Delete(role.Id);
        Assert.True(fixture.Effective.Has(bob.Id, "review.merge", project.Id));
    }

    [Fact]
    public void A_custom_role_cannot_exceed_the_organisation_ceiling()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var role = fixture.Roles.Create("Pretender", "pretender", ["organisation.destroy", "users.manage", "review.read"]);

        fixture.Projects.GrantUser(project.Id, bob.Id, role.Id);
        var permissions = fixture.Effective.ForUser(bob.Id, project.Id);

        Assert.Equal(new[] { "review.read" }, permissions.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void Leaving_a_team_drops_the_role_it_carried()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var team = fixture.Teams.Create("Readers", "readers", null, fixture.SystemRole(SystemAccessRoles.Reader).Id);
        fixture.Teams.AddMember(team.Id, bob.Id);
        fixture.Projects.GrantTeam(project.Id, team.Id);

        Assert.False(fixture.Effective.Has(bob.Id, "review.merge", project.Id));

        fixture.Teams.RemoveMember(team.Id, bob.Id);
        Assert.True(fixture.Effective.Has(bob.Id, "review.merge", project.Id));
    }

    [Fact]
    public void A_role_on_another_project_does_not_narrow_this_one()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var narrowed = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var open = fixture.Projects.CreateProject(new CreateProjectRequest("Shared", "shared", null, null, ProjectVisibility.Organisation), owner.Id);
        fixture.Projects.GrantUser(narrowed.Id, bob.Id, fixture.SystemRole(SystemAccessRoles.Reader).Id);

        Assert.False(fixture.Effective.Has(bob.Id, "review.merge", narrowed.Id));
        Assert.True(fixture.Effective.Has(bob.Id, "review.merge", open.Id));
    }

    [Fact]
    public void Promoting_the_organisation_role_raises_the_ceiling_the_project_role_is_capped_by()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var role = fixture.Roles.Create("Team Lead", "team-lead", ["teams.manage", "review.read"]);
        fixture.Projects.GrantUser(project.Id, bob.Id, role.Id);

        Assert.False(fixture.Effective.Has(bob.Id, "teams.manage", project.Id));

        fixture.Memberships.ChangeRole(bob.Id, OrganisationRole.Admin);
        Assert.True(fixture.Effective.Has(bob.Id, "teams.manage", project.Id));
    }

    [Fact]
    public void Intersect_keeps_only_permissions_present_in_both_sets()
    {
        var ceiling = OrganisationPermissions.ForRole(OrganisationRole.Member);
        var result = EffectivePermissionService.Intersect(ceiling, ["review.read", "organisation.destroy", "REVIEW.MERGE"]);

        Assert.Equal(new[] { "REVIEW.MERGE", "review.read" }, result.OrderBy(p => p, StringComparer.Ordinal));
    }
}
