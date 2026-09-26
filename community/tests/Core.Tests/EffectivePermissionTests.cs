using ForgeDeck.Core.Application;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Identity;

namespace Core.Tests;

/// <summary>
/// One organisation wired for additive RBAC scenarios: org role + project/team grants union.
/// </summary>
public sealed class EffectivePermissionScenario : IDisposable
{
    private static readonly string[] ProjectRoleSlugs =
    [
        SystemAccessRoles.Reader,
        SystemAccessRoles.Viewer,
        SystemAccessRoles.Developer,
        SystemAccessRoles.Reviewer,
        SystemAccessRoles.Builder,
        SystemAccessRoles.Deployer,
        SystemAccessRoles.ProjectAdmin
    ];

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

        foreach (var slug in ProjectRoleSlugs)
        {
            var role = _fixture.SystemRole(slug).Id;

            var viaTeamGrant = AddMember($"team-{slug}", OrganisationRole.Member);
            var grantTeam = _fixture.Teams.Create($"Grant {slug}", $"grant-{slug}", null);
            _fixture.Teams.AddMember(grantTeam.Id, viaTeamGrant, assignDefaultTeamRole: false);
            _fixture.Projects.GrantTeam(Private, grantTeam.Id, role);
            _teamGrantUsers[slug] = viaTeamGrant;

            var viaDirectGrant = AddMember($"direct-{slug}", OrganisationRole.Member);
            _fixture.Projects.GrantUser(Private, viaDirectGrant, role);
            _directGrantUsers[slug] = viaDirectGrant;

            var admin = AddMember($"admin-{slug}", OrganisationRole.Admin);
            var adminTeam = _fixture.Teams.Create($"Admin {slug}", $"admin-team-{slug}", null);
            _fixture.Teams.AddMember(adminTeam.Id, admin, assignDefaultTeamRole: false);
            _fixture.Projects.GrantTeam(Private, adminTeam.Id, role);
            _adminUsers[slug] = admin;

            var viaTeamRole = AddMember($"group-{slug}", OrganisationRole.Member);
            var roleTeam = _fixture.Teams.Create($"Group {slug}", $"group-team-{slug}", null, role);
            _fixture.Teams.AddMember(roleTeam.Id, viaTeamRole, assignDefaultTeamRole: false);
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
        _fixture.Teams.AddMember(ownerTeam.Id, Owner, assignDefaultTeamRole: false);
        _fixture.Projects.GrantTeam(Private, ownerTeam.Id);

        UnionMember = AddMember("union", OrganisationRole.Member);
        _fixture.Projects.GrantUser(Private, UnionMember, _fixture.SystemRole(SystemAccessRoles.Reviewer).Id);
        var unionTeam = _fixture.Teams.Create("Union Builders", "union-builders", null);
        _fixture.Teams.AddMember(unionTeam.Id, UnionMember, assignDefaultTeamRole: false);
        _fixture.Projects.GrantTeam(Private, unionTeam.Id, _fixture.SystemRole(SystemAccessRoles.Builder).Id);

        OverrideMember = AddMember("override", OrganisationRole.Member);
        var overrideTeam = _fixture.Teams.Create("Override", "override-team", null, _fixture.SystemRole(SystemAccessRoles.Deployer).Id);
        _fixture.Teams.AddMember(overrideTeam.Id, OverrideMember, assignDefaultTeamRole: false);
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
    public static IEnumerable<object[]> ProjectRoles() =>
        new[]
        {
            SystemAccessRoles.Reader,
            SystemAccessRoles.Viewer,
            SystemAccessRoles.Developer,
            SystemAccessRoles.Reviewer,
            SystemAccessRoles.Builder,
            SystemAccessRoles.Deployer,
            SystemAccessRoles.ProjectAdmin
        }.Select(slug => new object[] { slug });

    public static IEnumerable<object[]> MemberUnionPairs()
    {
        foreach (var slug in new[]
                 {
                     SystemAccessRoles.Reader, SystemAccessRoles.Viewer, SystemAccessRoles.Developer,
                     SystemAccessRoles.Reviewer, SystemAccessRoles.Builder, SystemAccessRoles.Deployer,
                     SystemAccessRoles.ProjectAdmin
                 })
        {
            var expected = AccessMatrix.Union(SystemAccessRoles.Member, slug);
            foreach (var permission in AccessMatrix.AllPermissions)
            {
                yield return [slug, permission, expected.Contains(permission)];
            }
        }
    }

    public static IEnumerable<object[]> AdminUnionPairs()
    {
        foreach (var slug in new[]
                 {
                     SystemAccessRoles.Reader, SystemAccessRoles.Viewer, SystemAccessRoles.Developer,
                     SystemAccessRoles.Reviewer, SystemAccessRoles.Builder, SystemAccessRoles.Deployer,
                     SystemAccessRoles.ProjectAdmin
                 })
        {
            var expected = AccessMatrix.Union(SystemAccessRoles.Admin, slug);
            foreach (var permission in AccessMatrix.AllPermissions)
            {
                yield return [slug, permission, expected.Contains(permission)];
            }
        }
    }

    [Theory]
    [MemberData(nameof(MemberUnionPairs))]
    public void Member_with_a_role_on_a_team_grant_gets_member_union_role(string slug, string permission, bool expected) =>
        Assert.Equal(expected, scenario.Effective.Has(scenario.TeamGrantUser(slug), permission, scenario.Private));

    [Theory]
    [MemberData(nameof(MemberUnionPairs))]
    public void Member_with_a_role_on_a_direct_grant_gets_member_union_role(string slug, string permission, bool expected) =>
        Assert.Equal(expected, scenario.Effective.Has(scenario.DirectGrantUser(slug), permission, scenario.Private));

    [Theory]
    [MemberData(nameof(MemberUnionPairs))]
    public void Member_inherits_the_role_attached_to_their_group(string slug, string permission, bool expected) =>
        Assert.Equal(expected, scenario.Effective.Has(scenario.TeamRoleOnlyUser(slug), permission, scenario.Private));

    [Theory]
    [MemberData(nameof(AdminUnionPairs))]
    public void Admin_permissions_union_with_the_granted_role(string slug, string permission, bool expected) =>
        Assert.Equal(expected, scenario.Effective.Has(scenario.AdminUser(slug), permission, scenario.Private));

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Owner_keeps_full_owner_permissions_even_when_in_a_reader_team(string permission)
    {
        var expected = AccessMatrix.Union(SystemAccessRoles.Owner, SystemAccessRoles.Reader).Contains(permission);
        Assert.Equal(expected, scenario.Effective.Has(scenario.Owner, permission, scenario.Private));
    }

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Owner_keeps_the_full_organisation_role_without_a_project_context(string permission) =>
        Assert.Equal(
            AccessMatrix.Expected(SystemAccessRoles.Owner).Contains(permission),
            scenario.Effective.Has(scenario.Owner, permission));

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Role_free_grant_leaves_only_organisation_member_permissions(string permission) =>
        Assert.Equal(
            AccessMatrix.Expected(SystemAccessRoles.Member).Contains(permission),
            scenario.Effective.Has(scenario.RoleFreeGrantMember, permission, scenario.Private));

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Organisation_visible_project_without_grants_uses_organisation_member_permissions(string permission) =>
        Assert.Equal(
            AccessMatrix.Expected(SystemAccessRoles.Member).Contains(permission),
            scenario.Effective.Has(scenario.RoleFreeGrantMember, permission, scenario.Shared));

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Member_without_a_grant_still_resolves_organisation_permissions(string permission) =>
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
    public void Multiple_roles_combine_additively(string permission)
    {
        var expected = AccessMatrix.Union(
            SystemAccessRoles.Member,
            SystemAccessRoles.Reviewer,
            SystemAccessRoles.Builder).Contains(permission);

        Assert.Equal(expected, scenario.Effective.Has(scenario.UnionMember, permission, scenario.Private));
    }

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Role_on_the_grant_replaces_the_role_on_the_group(string permission) =>
        Assert.Equal(
            AccessMatrix.Union(SystemAccessRoles.Member, SystemAccessRoles.Reader).Contains(permission),
            scenario.Effective.Has(scenario.OverrideMember, permission, scenario.Private));

    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void An_empty_role_adds_no_project_permissions(string permission)
    {
        Assert.Equal(
            AccessMatrix.Expected(SystemAccessRoles.Member).Contains(permission),
            scenario.Effective.Has(scenario.EmptyRoleMember, permission, scenario.Private));
        Assert.Equal(
            AccessMatrix.Expected(SystemAccessRoles.Member).Contains(permission),
            scenario.Effective.Has(scenario.EmptyRoleMember, permission));
    }

    [Theory]
    [InlineData("ORGANISATION.READ")]
    [InlineData("Review.Read")]
    [InlineData("deploy.READ")]
    public void Effective_permission_checks_ignore_casing(string permission) =>
        Assert.True(scenario.Effective.Has(scenario.TeamGrantUser(SystemAccessRoles.Reader), permission, scenario.Private));

    [Fact]
    public void Unknown_project_context_falls_back_to_organisation_permissions() =>
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
    public void Removing_the_role_from_a_grant_drops_project_role_permissions()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var reader = fixture.SystemRole(SystemAccessRoles.Reader);

        fixture.Projects.GrantUser(project.Id, bob.Id, reader.Id);
        Assert.True(fixture.Effective.Has(bob.Id, "review.read", project.Id));

        fixture.Projects.GrantUser(project.Id, bob.Id);
        Assert.False(fixture.Effective.Has(bob.Id, "review.read", project.Id));
        Assert.True(fixture.Effective.Has(bob.Id, OrganisationPermissions.OrganisationRead, project.Id));
    }

    [Fact]
    public void Deleting_a_custom_role_drops_its_permissions()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var role = fixture.Roles.Create("Reader Only", "reader-only", ["review.read"]);

        fixture.Projects.GrantUser(project.Id, bob.Id, role.Id);
        Assert.True(fixture.Effective.Has(bob.Id, "review.read", project.Id));

        fixture.Roles.Delete(role.Id);
        Assert.False(fixture.Effective.Has(bob.Id, "review.read", project.Id));
    }

    [Fact]
    public void A_custom_project_role_cannot_smuggle_organisation_only_permissions()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var role = fixture.Roles.Create("Pretender", "pretender", ["organisation.destroy", "users.manage", "review.read"]);

        fixture.Projects.GrantUser(project.Id, bob.Id, role.Id);
        var permissions = fixture.Effective.ForUser(bob.Id, project.Id);

        Assert.Contains("review.read", permissions);
        Assert.DoesNotContain("organisation.destroy", permissions);
        Assert.DoesNotContain("users.manage", permissions);
    }

    [Fact]
    public void Leaving_a_team_drops_inherited_project_permissions()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var team = fixture.Teams.Create("Readers", "readers", null, fixture.SystemRole(SystemAccessRoles.Reader).Id);
        fixture.Teams.AddMember(team.Id, bob.Id);
        fixture.Projects.GrantTeam(project.Id, team.Id);

        Assert.True(fixture.Effective.Has(bob.Id, "review.read", project.Id));

        fixture.Teams.RemoveMember(team.Id, bob.Id);
        Assert.False(fixture.Effective.Has(bob.Id, "review.read", project.Id));
    }

    [Fact]
    public void A_role_on_another_project_does_not_apply_here()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var narrowed = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var open = fixture.Projects.CreateProject(new CreateProjectRequest("Shared", "shared", null, null, ProjectVisibility.Organisation), owner.Id);
        fixture.Projects.GrantUser(narrowed.Id, bob.Id, fixture.SystemRole(SystemAccessRoles.Reader).Id);

        Assert.True(fixture.Effective.Has(bob.Id, "review.read", narrowed.Id));
        Assert.False(fixture.Effective.Has(bob.Id, "review.read", open.Id));
    }

    [Fact]
    public void Direct_organisation_grant_adds_permission_without_promoting_role()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);

        Assert.False(fixture.Effective.Has(bob.Id, OrganisationPermissions.ExtensionsInstall));

        fixture.Roles.GrantPermission(bob.Id, OrganisationPermissions.ExtensionsInstall, ScopeType.Organisation, null, owner.Id);
        Assert.True(fixture.Effective.Has(bob.Id, OrganisationPermissions.ExtensionsInstall));
        Assert.False(fixture.Effective.Has(bob.Id, OrganisationPermissions.UsersManage));
    }

    [Fact]
    public async Task Team_lead_role_grants_team_scoped_permissions()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var alice = fixture.Memberships.Add("alice@example.com", "alice", "Alice", "password123", OrganisationRole.Member, owner.Id);
        var team = fixture.Teams.Create("Platform", "platform", null);
        var lead = fixture.SystemRole(SystemAccessRoles.TeamLead);
        fixture.Teams.AddMember(team.Id, alice.Id, lead.Id, owner.Id);

        Assert.True(fixture.Effective.Service.Has(alice.Id, OrganisationPermissions.TeamProjectsCreate));
        var explanation = await fixture.Effective.Service.ExplainAsync(
            alice.Id,
            OrganisationPermissions.TeamProjectsCreate,
            ResourceScope.ForTeam(team.Id));
        Assert.True(explanation.Granted);
        Assert.Contains(explanation.Sources, s => s.Kind == "team_role" && s.RoleSlug == SystemAccessRoles.TeamLead);
    }

    [Fact]
    public void Expired_team_membership_stops_inherited_project_access()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var alice = fixture.Memberships.Add("alice@example.com", "alice", "Alice", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var team = fixture.Teams.Create("Platform", "platform", null);
        fixture.Teams.AddMember(team.Id, alice.Id, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        fixture.Projects.GrantTeam(project.Id, team.Id, fixture.SystemRole(SystemAccessRoles.Developer).Id);

        Assert.False(fixture.Effective.Has(alice.Id, "pipelines.run", project.Id));
        fixture.Projects.GrantUser(project.Id, alice.Id, fixture.SystemRole(SystemAccessRoles.Viewer).Id);
        Assert.True(fixture.Effective.Has(alice.Id, "review.read", project.Id));
        Assert.False(fixture.Effective.Has(alice.Id, "pipelines.run", project.Id));
    }

    [Fact]
    public void Final_owner_cannot_be_expired()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        Assert.Throws<InvalidOperationException>(() =>
            fixture.Memberships.SetExpiration(owner.Id, DateTimeOffset.UtcNow.AddDays(-1)));
    }
}
