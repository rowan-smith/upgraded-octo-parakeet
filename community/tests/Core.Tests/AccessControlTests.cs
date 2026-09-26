using ForgeDeck.Core.Application;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Identity;

namespace Core.Tests;

/// <summary>
/// Expected permission sets for the built-in roles, written out longhand so a change to
/// <see cref="SystemAccessRoles"/> has to be a deliberate change to this specification too.
/// </summary>
internal static class AccessMatrix
{
    public static readonly string[] ModulePermissions =
    [
        "source.repository.read", "source.repository.connect",
        "review.read", "review.comment", "review.request", "review.approve", "review.merge", "review.manage",
        "git.repository.create", "git.repository.push", "git.repository.read",
        "pipelines.manage", "pipelines.run", "pipelines.read", "pipelines.cancel",
        "pipelines.runner.read", "pipelines.runner.manage",
        "deploy.read", "deploy.execute", "deploy.manage", "deploy.approve"
    ];

    public static readonly string[] Member =
    [
        "organisation.read", "users.read", "teams.read.assigned", "projects.read.accessible", "project.read"
    ];

    public static readonly string[] Admin =
    [
        ..Member,
        "users.invite", "users.manage",
        "teams.read", "teams.create", "teams.manage",
        "projects.read", "projects.create", "projects.manage",
        "extensions.read", "core.integration.manage", "connectors.read", "audit.read", "roles.manage",
        ..ModulePermissions
    ];

    public static readonly string[] Owner =
    [
        ..Admin,
        "organisation.manage", "organisation.settings.manage",
        "modules.manage",
        "extensions.install", "extensions.enable", "extensions.disable", "extensions.uninstall",
        "connectors.manage",
        "licensing.read", "licensing.manage",
        "permissions.manage",
        "organisation.destroy"
    ];

    public static readonly string[] Viewer =
    [
        "project.read", "organisation.read", "source.repository.read", "review.read",
        "git.repository.read", "pipelines.read", "deploy.read"
    ];

    public static readonly string[] Reader = Viewer;

    public static readonly string[] Developer =
    [
        ..Viewer,
        "review.comment", "review.request",
        "pipelines.run",
        "git.repository.push"
    ];

    public static readonly string[] Reviewer = [..Viewer, "review.comment", "review.request", "review.approve"];

    public static readonly string[] Builder =
    [
        ..Viewer,
        "pipelines.run", "pipelines.manage", "pipelines.cancel", "pipelines.runner.read"
    ];

    public static readonly string[] Deployer = [..Viewer, "deploy.execute", "deploy.manage", "deploy.approve"];

    public static readonly string[] DeployOperator = Deployer;

    public static readonly string[] ProjectAdmin =
    [
        ..Developer,
        "project.settings.manage", "project.members.manage", "project.permissions.manage",
        "source.repository.connect",
        "review.approve", "review.merge", "review.manage",
        "git.repository.create",
        "pipelines.manage", "pipelines.cancel", "pipelines.runner.manage",
        "deploy.execute", "deploy.manage", "deploy.approve"
    ];

    public static readonly string[] TeamLead =
    [
        "team.read", "team.settings.manage", "team.members.read", "team.members.manage",
        "team.roles.read", "team.roles.manage", "team.projects.read", "team.projects.create", "team.projects.manage"
    ];

    public static readonly string[] TeamMember = ["team.read", "team.members.read", "team.projects.read"];

    public static readonly string[] AllPermissions = Owner.Concat(TeamLead).Concat(ProjectAdmin).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static readonly Dictionary<string, string[]> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        [SystemAccessRoles.Owner] = Owner,
        [SystemAccessRoles.Admin] = Admin,
        [SystemAccessRoles.Member] = Member,
        [SystemAccessRoles.TeamLead] = TeamLead,
        [SystemAccessRoles.TeamMember] = TeamMember,
        [SystemAccessRoles.ProjectAdmin] = ProjectAdmin,
        [SystemAccessRoles.Reader] = Reader,
        [SystemAccessRoles.Viewer] = Viewer,
        [SystemAccessRoles.Developer] = Developer,
        [SystemAccessRoles.Reviewer] = Reviewer,
        [SystemAccessRoles.Builder] = Builder,
        [SystemAccessRoles.Deployer] = Deployer,
        [SystemAccessRoles.DeployOperator] = DeployOperator
    };

    public static IReadOnlySet<string> Expected(string slug) =>
        new HashSet<string>(Roles[slug], StringComparer.OrdinalIgnoreCase);

    /// <summary>Additive union of organisation membership permissions and a project/team role.</summary>
    public static IReadOnlySet<string> Union(string organisationRoleSlug, params string[] projectRoleSlugs)
    {
        var result = new HashSet<string>(Roles[organisationRoleSlug], StringComparer.OrdinalIgnoreCase);
        foreach (var slug in projectRoleSlugs)
        {
            result.UnionWith(Roles[slug]);
        }

        return result;
    }

    [Obsolete("Additive RBAC replaces ceiling intersection.")]
    public static IReadOnlySet<string> Intersection(string slug, string[] ceiling)
    {
        var limit = new HashSet<string>(ceiling, StringComparer.OrdinalIgnoreCase);
        return new HashSet<string>(Roles[slug].Where(limit.Contains), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every (role, permission) pair with the answer the role is expected to give.</summary>
    public static IEnumerable<object[]> RolePermissionPairs()
    {
        foreach (var (slug, permissions) in Roles)
        {
            var granted = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            foreach (var permission in AllPermissions)
            {
                yield return [slug, permission, granted.Contains(permission)];
            }
        }
    }

    public static IEnumerable<object[]> Permissions() => AllPermissions.Select(permission => new object[] { permission });

    public static IEnumerable<object[]> Slugs() => Roles.Keys.Select(slug => new object[] { slug });
}

public sealed class SystemAccessRoleMatrixTests
{
    [Theory]
    [MemberData(nameof(AccessMatrix.RolePermissionPairs), MemberType = typeof(AccessMatrix))]
    public void Every_system_role_grants_exactly_its_specified_permissions(string slug, string permission, bool expected) =>
        Assert.Equal(expected, SystemAccessRoles.PermissionsFor(slug).Contains(permission));

    [Theory]
    [MemberData(nameof(AccessMatrix.Slugs), MemberType = typeof(AccessMatrix))]
    public void System_role_permission_count_matches_specification(string slug) =>
        Assert.Equal(AccessMatrix.Expected(slug).Count, SystemAccessRoles.PermissionsFor(slug).Count);

    [Theory]
    [MemberData(nameof(AccessMatrix.Slugs), MemberType = typeof(AccessMatrix))]
    public void System_role_permissions_are_all_in_the_catalogue(string slug) =>
        Assert.All(SystemAccessRoles.PermissionsFor(slug), permission => Assert.True(PermissionCatalogue.IsKnown(permission), permission));

    [Theory]
    [MemberData(nameof(AccessMatrix.Slugs), MemberType = typeof(AccessMatrix))]
    public void System_role_is_defined_with_name_and_description(string slug)
    {
        var definition = SystemAccessRoles.Find(slug);
        Assert.NotNull(definition);
        Assert.False(string.IsNullOrWhiteSpace(definition!.Name));
        Assert.False(string.IsNullOrWhiteSpace(definition.Description));
    }

    [Theory]
    [InlineData("OWNER")]
    [InlineData("Owner")]
    [InlineData("oWnEr")]
    public void System_role_lookup_is_case_insensitive(string slug) =>
        Assert.Equal(SystemAccessRoles.Owner, SystemAccessRoles.Find(slug)!.Slug);

    [Fact]
    public void Thirteen_system_roles_are_defined() => Assert.Equal(13, SystemAccessRoles.Definitions.Count);

    [Fact]
    public void Unknown_system_role_slug_is_rejected() =>
        Assert.Throws<KeyNotFoundException>(() => SystemAccessRoles.PermissionsFor("architect"));

    [Theory]
    [InlineData(OrganisationRole.Owner, SystemAccessRoles.Owner)]
    [InlineData(OrganisationRole.Admin, SystemAccessRoles.Admin)]
    [InlineData(OrganisationRole.Member, SystemAccessRoles.Member)]
    public void Organisation_roles_map_onto_matching_system_roles(OrganisationRole role, string slug)
    {
        Assert.Equal(slug, SystemAccessRoles.SlugFor(role));
        Assert.Equal(OrganisationPermissions.ForRole(role).OrderBy(p => p), SystemAccessRoles.PermissionsFor(slug).OrderBy(p => p));
    }

    [Theory]
    [InlineData(SystemAccessRoles.Reader)]
    [InlineData(SystemAccessRoles.Developer)]
    [InlineData(SystemAccessRoles.Reviewer)]
    [InlineData(SystemAccessRoles.Builder)]
    [InlineData(SystemAccessRoles.Deployer)]
    [InlineData(SystemAccessRoles.Viewer)]
    [InlineData(SystemAccessRoles.ProjectAdmin)]
    public void Project_roles_do_not_include_organisation_only_permissions(string slug) =>
        Assert.All(SystemAccessRoles.PermissionsFor(slug), permission =>
            Assert.True(
                PermissionCatalogue.AllowsScope(permission, ScopeType.Project)
                || PermissionCatalogue.AllowsScope(permission, ScopeType.Organisation),
                permission));

    [Theory]
    [InlineData(SystemAccessRoles.Reader, "deploy.execute")]
    [InlineData(SystemAccessRoles.Reader, "review.merge")]
    [InlineData(SystemAccessRoles.Reader, "pipelines.run")]
    [InlineData(SystemAccessRoles.Developer, "deploy.execute")]
    [InlineData(SystemAccessRoles.Developer, "deploy.manage")]
    [InlineData(SystemAccessRoles.Reviewer, "review.merge")]
    [InlineData(SystemAccessRoles.Reviewer, "review.manage")]
    [InlineData(SystemAccessRoles.Builder, "deploy.execute")]
    [InlineData(SystemAccessRoles.Builder, "pipelines.runner.manage")]
    [InlineData(SystemAccessRoles.Deployer, "pipelines.run")]
    public void Documented_exclusions_hold(string slug, string permission) =>
        Assert.False(SystemAccessRoles.PermissionsFor(slug).Contains(permission));

    [Theory]
    [InlineData(SystemAccessRoles.Developer, "git.repository.push")]
    [InlineData(SystemAccessRoles.Developer, "pipelines.run")]
    [InlineData(SystemAccessRoles.Reviewer, "review.approve")]
    [InlineData(SystemAccessRoles.Reviewer, "review.request")]
    [InlineData(SystemAccessRoles.Reviewer, "review.comment")]
    [InlineData(SystemAccessRoles.Builder, "pipelines.manage")]
    [InlineData(SystemAccessRoles.Builder, "pipelines.cancel")]
    [InlineData(SystemAccessRoles.Builder, "pipelines.runner.read")]
    [InlineData(SystemAccessRoles.Deployer, "deploy.manage")]
    [InlineData(SystemAccessRoles.Deployer, "deploy.execute")]
    [InlineData(SystemAccessRoles.TeamLead, "team.projects.create")]
    [InlineData(SystemAccessRoles.ProjectAdmin, "project.permissions.manage")]
    public void Documented_inclusions_hold(string slug, string permission) =>
        Assert.True(SystemAccessRoles.PermissionsFor(slug).Contains(permission));
}

public sealed class PermissionCatalogueTests
{
    [Theory]
    [MemberData(nameof(AccessMatrix.Permissions), MemberType = typeof(AccessMatrix))]
    public void Catalogue_describes_every_permission(string permission)
    {
        Assert.True(PermissionCatalogue.IsKnown(permission));
        var descriptor = PermissionCatalogue.All.Single(entry => entry.Key == permission);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Category));
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Title));
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
    }

    [Fact]
    public void Catalogue_has_no_duplicates() =>
        Assert.Equal(PermissionCatalogue.All.Count, PermissionCatalogue.AllKeys.Count);

    [Fact]
    public void Catalogue_covers_every_owner_permission() =>
        Assert.All(
            OrganisationPermissions.ForRole(OrganisationRole.Owner),
            permission => Assert.True(PermissionCatalogue.IsKnown(permission), permission));

    [Theory]
    [InlineData("Organisation")]
    [InlineData("People")]
    [InlineData("Projects")]
    [InlineData("Platform")]
    [InlineData("Team")]
    [InlineData("Project")]
    [InlineData("Source")]
    [InlineData("Review")]
    [InlineData("Git")]
    [InlineData("Build")]
    [InlineData("Deploy")]
    public void Catalogue_groups_permissions_into_expected_categories(string category) =>
        Assert.Contains(category, PermissionCatalogue.Categories);

    [Theory]
    [InlineData("ORGANISATION.READ")]
    [InlineData("Organisation.Read")]
    [InlineData("deploy.EXECUTE")]
    [InlineData(" review.read ")]
    [InlineData("Pipelines.Runner.Manage")]
    public void Known_permissions_are_matched_case_insensitively_and_trimmed(string permission) =>
        Assert.True(PermissionCatalogue.IsKnown(permission));

    [Theory]
    [InlineData("organisation.delete")]
    [InlineData("review.publish")]
    [InlineData("deploy")]
    [InlineData("*")]
    [InlineData("admin")]
    [InlineData("pipelines.*")]
    public void Unknown_permissions_are_not_recognised(string permission) =>
        Assert.False(PermissionCatalogue.IsKnown(permission));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_permissions_are_not_recognised(string? permission) =>
        Assert.False(PermissionCatalogue.IsKnown(permission));

    [Theory]
    [InlineData("organisation.delete")]
    [InlineData("nonsense")]
    [InlineData("REVIEW.PUBLISH")]
    public void Normalise_rejects_unknown_permissions(string permission) =>
        Assert.Throws<ArgumentException>(() => PermissionCatalogue.Normalise([permission]));

    [Fact]
    public void Normalise_canonicalises_casing_and_removes_duplicates()
    {
        var result = PermissionCatalogue.Normalise(["REVIEW.READ", "review.read", " deploy.read ", ""]);
        Assert.Equal(new[] { "deploy.read", "review.read" }, result.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void Normalise_of_nothing_is_empty()
    {
        Assert.Empty(PermissionCatalogue.Normalise(null));
        Assert.Empty(PermissionCatalogue.Normalise([]));
    }

    [Theory]
    [InlineData(OrganisationRole.Owner, "ORGANISATION.DESTROY")]
    [InlineData(OrganisationRole.Owner, "Licensing.Manage")]
    [InlineData(OrganisationRole.Admin, "USERS.MANAGE")]
    [InlineData(OrganisationRole.Admin, "Teams.Manage")]
    [InlineData(OrganisationRole.Member, "Organisation.Read")]
    [InlineData(OrganisationRole.Member, "USERS.READ")]
    [InlineData(OrganisationRole.Member, "Project.Read")]
    public void Organisation_permission_checks_ignore_casing(OrganisationRole role, string permission) =>
        Assert.True(OrganisationPermissions.ForRole(role).Contains(permission));
}

public sealed class AccessRoleServiceTests
{
    [Theory]
    [MemberData(nameof(AccessMatrix.Slugs), MemberType = typeof(AccessMatrix))]
    public void System_roles_are_seeded_and_round_trip_through_storage(string slug)
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        var role = fixture.Roles.FindBySlug(slug);
        Assert.NotNull(role);
        Assert.True(role!.IsSystem);
        Assert.Equal(AccessMatrix.Expected(slug).OrderBy(p => p), role.Permissions.OrderBy(p => p));
    }

    [Fact]
    public void Seeding_is_idempotent()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        var first = fixture.Roles.List();
        fixture.Roles.EnsureSystemRoles();
        var second = new RoleService(fixture.Store).List();

        Assert.Equal(8, first.Count);
        Assert.Equal(8, second.Count);
        Assert.Equal(first.Select(r => r.Id).OrderBy(id => id), second.Select(r => r.Id).OrderBy(id => id));
    }

    [Fact]
    public void Listing_puts_system_roles_first()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        fixture.Roles.Create("Release Captain", "release-captain", ["deploy.read"]);

        var roles = fixture.Roles.List();
        Assert.Equal(9, roles.Count);
        Assert.True(roles.Take(8).All(role => role.IsSystem));
        Assert.False(roles[^1].IsSystem);
    }

    [Fact]
    public void Custom_role_is_created_with_normalised_permissions()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        var role = fixture.Roles.Create("Release Captain", null, ["DEPLOY.READ", "deploy.execute", "deploy.read"], "Ships releases");

        Assert.Equal("release-captain", role.Slug);
        Assert.False(role.IsSystem);
        Assert.Equal("Ships releases", role.Description);
        Assert.Equal(new[] { "deploy.execute", "deploy.read" }, role.Permissions.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void Custom_role_survives_a_reload()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var created = fixture.Roles.Create("Auditor", "auditor", ["audit.read", "organisation.read"]);

        var reloaded = new RoleService(fixture.Store).Find(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(new[] { "audit.read", "organisation.read" }, reloaded!.Permissions.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void Custom_role_may_have_no_permissions()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        var role = fixture.Roles.Create("Placeholder", "placeholder", []);

        Assert.Empty(role.Permissions);
        Assert.Empty(fixture.Roles.Find(role.Id)!.Permissions);
    }

    [Theory]
    [InlineData("organisation.delete")]
    [InlineData("review.publish")]
    [InlineData("deploy.everything")]
    [InlineData("*")]
    public void Unknown_permission_is_rejected_on_create(string permission)
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        Assert.Throws<ArgumentException>(() => fixture.Roles.Create("Bad", "bad", ["review.read", permission]));
        Assert.Null(fixture.Roles.FindBySlug("bad"));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("-x")]
    [InlineData("!!")]
    [InlineData("--")]
    public void Invalid_slug_is_rejected_on_create(string slug)
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        Assert.Throws<ArgumentException>(() => fixture.Roles.Create("Bad", slug, ["review.read"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_name_is_rejected_on_create(string name)
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        Assert.Throws<ArgumentException>(() => fixture.Roles.Create(name, "some-role", ["review.read"]));
    }

    [Theory]
    [MemberData(nameof(AccessMatrix.Slugs), MemberType = typeof(AccessMatrix))]
    public void Custom_role_cannot_reuse_a_system_slug(string slug)
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        Assert.Throws<ArgumentException>(() => fixture.Roles.Create("Clash", slug, ["review.read"]));
    }

    [Fact]
    public void Custom_role_slug_must_be_unique()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        fixture.Roles.Create("Auditor", "auditor", ["audit.read"]);

        Assert.Throws<ArgumentException>(() => fixture.Roles.Create("Another", "auditor", ["audit.read"]));
    }

    [Fact]
    public void Custom_role_update_replaces_permissions()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var role = fixture.Roles.Create("Auditor", "auditor", ["audit.read"]);

        var updated = fixture.Roles.Update(role.Id, "Compliance", ["organisation.read", "audit.read"], "Reads the trail");

        Assert.Equal("Compliance", updated.Name);
        Assert.Equal("Reads the trail", updated.Description);
        Assert.Equal(new[] { "audit.read", "organisation.read" }, updated.Permissions.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void Custom_role_update_can_clear_all_permissions()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var role = fixture.Roles.Create("Auditor", "auditor", ["audit.read"]);

        Assert.Empty(fixture.Roles.Update(role.Id, null, []).Permissions);
    }

    [Fact]
    public void Custom_role_update_leaves_permissions_alone_when_omitted()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var role = fixture.Roles.Create("Auditor", "auditor", ["audit.read"]);

        var updated = fixture.Roles.Update(role.Id, "Auditors", null);

        Assert.Equal("Auditors", updated.Name);
        Assert.Equal(new[] { "audit.read" }, updated.Permissions);
    }

    [Fact]
    public void Custom_role_update_can_change_slug()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var role = fixture.Roles.Create("Auditor", "auditor", ["audit.read"]);

        Assert.Equal("compliance", fixture.Roles.Update(role.Id, null, null, null, "compliance").Slug);
    }

    [Fact]
    public void Custom_role_update_rejects_a_taken_slug()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        fixture.Roles.Create("Auditor", "auditor", ["audit.read"]);
        var second = fixture.Roles.Create("Compliance", "compliance", ["audit.read"]);

        Assert.Throws<ArgumentException>(() => fixture.Roles.Update(second.Id, null, null, null, "auditor"));
    }

    [Theory]
    [InlineData("organisation.delete")]
    [InlineData("nonsense")]
    public void Unknown_permission_is_rejected_on_update(string permission)
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var role = fixture.Roles.Create("Auditor", "auditor", ["audit.read"]);

        Assert.Throws<ArgumentException>(() => fixture.Roles.Update(role.Id, null, [permission]));
        Assert.Equal(new[] { "audit.read" }, fixture.Roles.Find(role.Id)!.Permissions);
    }

    [Theory]
    [MemberData(nameof(AccessMatrix.Slugs), MemberType = typeof(AccessMatrix))]
    public void System_roles_cannot_be_deleted(string slug)
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        var role = fixture.SystemRole(slug);
        Assert.Throws<InvalidOperationException>(() => fixture.Roles.Delete(role.Id));
        Assert.NotNull(fixture.Roles.Find(role.Id));
    }

    [Theory]
    [MemberData(nameof(AccessMatrix.Slugs), MemberType = typeof(AccessMatrix))]
    public void System_roles_cannot_be_edited(string slug)
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        var role = fixture.SystemRole(slug);
        Assert.Throws<InvalidOperationException>(() => fixture.Roles.Update(role.Id, "Renamed", ["review.read"]));
        Assert.Equal(AccessMatrix.Expected(slug).OrderBy(p => p), fixture.Roles.Find(role.Id)!.Permissions.OrderBy(p => p));
    }

    [Fact]
    public void Custom_role_can_be_deleted()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var role = fixture.Roles.Create("Auditor", "auditor", ["audit.read"]);

        fixture.Roles.Delete(role.Id);

        Assert.Null(fixture.Roles.Find(role.Id));
        Assert.Equal(8, fixture.Roles.List().Count);
    }

    [Fact]
    public void Deleting_a_role_detaches_it_from_teams_and_grants()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var role = fixture.Roles.Create("Auditor", "auditor", ["audit.read"]);
        var team = fixture.Teams.Create("Compliance", "compliance", null, role.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        fixture.Projects.GrantUser(project.Id, bob.Id, role.Id);
        fixture.Projects.GrantTeam(project.Id, team.Id, role.Id);

        fixture.Roles.Delete(role.Id);

        Assert.Null(fixture.Teams.Get(team.Id)!.RoleId);
        Assert.Null(fixture.Store.ListProjectUserAccess(project.Id).Single(row => row.UserId == bob.Id).RoleId);
        Assert.Null(fixture.Store.ListProjectTeamAccess(project.Id).Single().RoleId);
    }

    [Fact]
    public void Missing_role_operations_report_not_found()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        Assert.Null(fixture.Roles.Find(Guid.NewGuid()));
        Assert.Null(fixture.Roles.FindBySlug("nope"));
        Assert.Throws<KeyNotFoundException>(() => fixture.Roles.Delete(Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => fixture.Roles.Update(Guid.NewGuid(), "x", null));
        Assert.Throws<KeyNotFoundException>(() => fixture.Roles.Require(Guid.NewGuid()));
    }

    [Fact]
    public void Require_accepts_null_and_known_roles()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var reader = fixture.SystemRole(SystemAccessRoles.Reader);

        Assert.Null(fixture.Roles.Require(null));
        Assert.Equal(reader.Id, fixture.Roles.Require(reader.Id));
    }
}

public sealed class TeamAndGrantRoleTests
{
    [Theory]
    [MemberData(nameof(AccessMatrix.Slugs), MemberType = typeof(AccessMatrix))]
    public void Team_can_be_created_with_any_role(string slug)
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var role = fixture.SystemRole(slug);

        var team = fixture.Teams.Create("Backend", "backend", null, role.Id);

        Assert.Equal(role.Id, team.RoleId);
        Assert.Equal(role.Id, fixture.Teams.Get(team.Id)!.RoleId);
    }

    [Theory]
    [MemberData(nameof(AccessMatrix.Slugs), MemberType = typeof(AccessMatrix))]
    public void Team_role_can_be_assigned_after_creation(string slug)
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var team = fixture.Teams.Create("Backend", "backend", null);
        var role = fixture.SystemRole(slug);

        fixture.Roles.AssignTeamRole(team.Id, role.Id);

        Assert.Equal(role.Id, fixture.Teams.Get(team.Id)!.RoleId);
    }

    [Fact]
    public void Team_role_can_be_cleared()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var reader = fixture.SystemRole(SystemAccessRoles.Reader);
        var team = fixture.Teams.Create("Backend", "backend", null, reader.Id);

        fixture.Roles.AssignTeamRole(team.Id, null);

        Assert.Null(fixture.Teams.Get(team.Id)!.RoleId);
    }

    [Fact]
    public void Team_update_keeps_the_role_unless_asked_to_change_it()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var reader = fixture.SystemRole(SystemAccessRoles.Reader);
        var team = fixture.Teams.Create("Backend", "backend", null, reader.Id);

        Assert.Equal(reader.Id, fixture.Teams.Update(team.Id, "Backend Team", null, null).RoleId);
        Assert.Null(fixture.Teams.Update(team.Id, null, null, null, null, clearRole: true).RoleId);
    }

    [Fact]
    public void Unknown_role_cannot_be_attached()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var team = fixture.Teams.Create("Backend", "backend", null);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var missing = Guid.NewGuid();

        Assert.Throws<KeyNotFoundException>(() => fixture.Teams.Create("Other", "other", null, missing));
        Assert.Throws<KeyNotFoundException>(() => fixture.Teams.Update(team.Id, null, null, null, missing));
        Assert.Throws<KeyNotFoundException>(() => fixture.Roles.AssignTeamRole(team.Id, missing));
        Assert.Throws<KeyNotFoundException>(() => fixture.Projects.GrantUser(project.Id, owner.Id, missing));
        Assert.Throws<KeyNotFoundException>(() => fixture.Projects.GrantTeam(project.Id, team.Id, missing));
    }

    [Fact]
    public void Assigning_a_role_to_an_unknown_team_is_not_found()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var reader = fixture.SystemRole(SystemAccessRoles.Reader);

        Assert.Throws<KeyNotFoundException>(() => fixture.Roles.AssignTeamRole(Guid.NewGuid(), reader.Id));
    }

    [Theory]
    [MemberData(nameof(AccessMatrix.Slugs), MemberType = typeof(AccessMatrix))]
    public void Project_user_grant_records_its_role(string slug)
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var role = fixture.SystemRole(slug);

        fixture.Projects.GrantUser(project.Id, bob.Id, role.Id);

        Assert.Equal(role.Id, fixture.Store.ListProjectUserAccess(project.Id).Single(row => row.UserId == bob.Id).RoleId);
    }

    [Theory]
    [MemberData(nameof(AccessMatrix.Slugs), MemberType = typeof(AccessMatrix))]
    public void Project_team_grant_records_its_role(string slug)
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var team = fixture.Teams.Create("Backend", "backend", null);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var role = fixture.SystemRole(slug);

        fixture.Projects.GrantTeam(project.Id, team.Id, role.Id);

        Assert.Equal(role.Id, fixture.Store.ListProjectTeamAccess(project.Id).Single().RoleId);
    }

    [Fact]
    public void Re_granting_replaces_the_role_on_an_existing_grant()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);
        var reader = fixture.SystemRole(SystemAccessRoles.Reader);
        var builder = fixture.SystemRole(SystemAccessRoles.Builder);

        fixture.Projects.GrantUser(project.Id, bob.Id, reader.Id);
        fixture.Projects.GrantUser(project.Id, bob.Id, builder.Id);

        var grants = fixture.Store.ListProjectUserAccess(project.Id).Where(row => row.UserId == bob.Id).ToArray();
        Assert.Single(grants);
        Assert.Equal(builder.Id, grants[0].RoleId);
    }

    [Fact]
    public void Grants_without_a_role_stay_role_free()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var team = fixture.Teams.Create("Backend", "backend", null);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", null, null), owner.Id);

        fixture.Projects.GrantUser(project.Id, bob.Id);
        fixture.Projects.GrantTeam(project.Id, team.Id);

        Assert.Null(fixture.Store.ListProjectUserAccess(project.Id).Single(row => row.UserId == bob.Id).RoleId);
        Assert.Null(fixture.Store.ListProjectTeamAccess(project.Id).Single().RoleId);
    }

    [Fact]
    public void The_last_active_owner_cannot_be_demoted_or_removed()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();

        Assert.Throws<InvalidOperationException>(() => fixture.Memberships.ChangeRole(owner.Id, OrganisationRole.Admin));
        Assert.Throws<InvalidOperationException>(() => fixture.Memberships.ChangeStatus(owner.Id, MembershipStatus.Suspended));
        Assert.Throws<InvalidOperationException>(() => fixture.Memberships.Remove(owner.Id));
        Assert.Equal(OrganisationRole.Owner, fixture.Store.GetMembership(owner.Id)!.Role);
    }
}
