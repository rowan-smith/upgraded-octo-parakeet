using ForgeDeck.Core.Domain;

namespace ForgeDeck.Core.Identity;

/// <summary>Seed definition for a built-in access role.</summary>
public sealed record SystemAccessRoleDefinition(
    string Slug,
    string Name,
    string Description,
    ScopeType ScopeType,
    IReadOnlySet<string> Permissions,
    string? DefaultProjectRoleSlug = null);

/// <summary>
/// Built-in access roles seeded on first use. Organisation roles mirror <see cref="OrganisationRole"/>;
/// team and project roles supply additive grants inside their scopes.
/// </summary>
public static class SystemAccessRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";
    public const string Reader = "reader";
    public const string Viewer = "viewer";
    public const string Developer = "developer";
    public const string Reviewer = "reviewer";
    public const string Builder = "builder";
    public const string Deployer = "deployer";
    public const string DeployOperator = "deploy-operator";
    public const string ProjectAdmin = "project-admin";
    public const string TeamLead = "team-lead";
    public const string TeamMember = "team-member";

    private static readonly HashSet<string> ViewerPermissions = Set(
    [
        OrganisationPermissions.ProjectRead,
        OrganisationPermissions.OrganisationRead,
        "source.repository.read",
        "review.read",
        "git.repository.read",
        "pipelines.read",
        "deploy.read"
    ]);

    private static readonly HashSet<string> DeveloperPermissions = Set(
    [
        ..ViewerPermissions,
        "review.comment", "review.request",
        "pipelines.run",
        "git.repository.push"
    ]);

    private static readonly HashSet<string> ReviewerPermissions = Set(
        [..ViewerPermissions, "review.comment", "review.request", "review.approve"]);

    private static readonly HashSet<string> BuilderPermissions = Set(
        [..ViewerPermissions, "pipelines.run", "pipelines.manage", "pipelines.cancel", "pipelines.runner.read"]);

    private static readonly HashSet<string> DeployerPermissions = Set(
        [..ViewerPermissions, "deploy.execute", "deploy.manage", "deploy.approve"]);

    private static readonly HashSet<string> ProjectAdminPermissions = Set(
    [
        ..DeveloperPermissions,
        OrganisationPermissions.ProjectSettingsManage,
        OrganisationPermissions.ProjectMembersManage,
        OrganisationPermissions.ProjectPermissionsManage,
        "source.repository.connect",
        "review.approve", "review.merge", "review.manage",
        "git.repository.create",
        "pipelines.manage", "pipelines.cancel", "pipelines.runner.manage",
        "deploy.execute", "deploy.manage", "deploy.approve"
    ]);

    private static readonly HashSet<string> TeamLeadPermissions = Set(
    [
        OrganisationPermissions.TeamRead,
        OrganisationPermissions.TeamSettingsManage,
        OrganisationPermissions.TeamMembersRead,
        OrganisationPermissions.TeamMembersManage,
        OrganisationPermissions.TeamRolesRead,
        OrganisationPermissions.TeamRolesManage,
        OrganisationPermissions.TeamProjectsRead,
        OrganisationPermissions.TeamProjectsCreate,
        OrganisationPermissions.TeamProjectsManage
    ]);

    private static readonly HashSet<string> TeamMemberPermissions = Set(
    [
        OrganisationPermissions.TeamRead,
        OrganisationPermissions.TeamMembersRead,
        OrganisationPermissions.TeamProjectsRead
    ]);

    private static readonly SystemAccessRoleDefinition[] DefinitionList =
    [
        new(Owner, "Owner", "Full organisation control including licensing, modules, and destructive actions.",
            ScopeType.Organisation, OrganisationPermissions.ForRole(OrganisationRole.Owner)),
        new(Admin, "Admin", "Manage projects, people, teams, and integrations.",
            ScopeType.Organisation, OrganisationPermissions.ForRole(OrganisationRole.Admin)),
        new(Member, "Member", "Minimal organisation membership; project work comes from team and project grants.",
            ScopeType.Organisation, OrganisationPermissions.ForRole(OrganisationRole.Member)),
        new(TeamLead, "Team Lead", "Manage team membership, roles, and team-owned projects.",
            ScopeType.Team, TeamLeadPermissions, Developer),
        new(TeamMember, "Team Member", "View the team and its projects.",
            ScopeType.Team, TeamMemberPermissions, Viewer),
        new(ProjectAdmin, "Project Admin", "Full control of a project including members and permissions.",
            ScopeType.Project, ProjectAdminPermissions),
        new(Developer, "Developer", "Contribute code, reviews, and builds on a project.",
            ScopeType.Project, DeveloperPermissions),
        new(Reviewer, "Reviewer", "Comment and approve pull requests.",
            ScopeType.Project, ReviewerPermissions),
        new(Viewer, "Viewer", "Read-only visibility across project modules.",
            ScopeType.Project, ViewerPermissions),
        new(Reader, "Reader", "Alias of Viewer for backwards compatibility.",
            ScopeType.Project, ViewerPermissions),
        new(Builder, "Builder", "Run and manage pipelines.",
            ScopeType.Project, BuilderPermissions),
        new(Deployer, "Deployer", "Execute and manage deployments.",
            ScopeType.Project, DeployerPermissions),
        new(DeployOperator, "Deploy Operator", "Execute deployments with approval where required.",
            ScopeType.Project, DeployerPermissions)
    ];

    public static IReadOnlyList<SystemAccessRoleDefinition> Definitions => DefinitionList;

    public static IReadOnlyList<string> Slugs => DefinitionList.Select(d => d.Slug).ToArray();

    public static SystemAccessRoleDefinition? Find(string slug) =>
        DefinitionList.FirstOrDefault(d => string.Equals(d.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlySet<string> PermissionsFor(string slug) =>
        Find(slug)?.Permissions ?? throw new KeyNotFoundException($"Unknown system role '{slug}'.");

    /// <summary>Maps an organisation membership role onto the equivalent system role slug.</summary>
    public static string SlugFor(OrganisationRole role) => role switch
    {
        OrganisationRole.Owner => Owner,
        OrganisationRole.Admin => Admin,
        _ => Member
    };

    private static HashSet<string> Set(IEnumerable<string> permissions) => new(permissions, StringComparer.OrdinalIgnoreCase);
}
