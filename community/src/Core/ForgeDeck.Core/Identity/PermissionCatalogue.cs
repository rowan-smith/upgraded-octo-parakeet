using ForgeDeck.Core.Domain;

namespace ForgeDeck.Core.Identity;

/// <summary>One entry per grantable permission. Drives role editors and validation of custom roles.</summary>
public sealed record PermissionDescriptor(
    string Key,
    string Category,
    string Title,
    string Description,
    IReadOnlyList<ScopeType> AllowedScopes,
    string? SourceExtensionId = null,
    string? RiskLevel = null);

/// <summary>Core permission catalogue. Modules register additional definitions through <see cref="IPermissionDefinitionRegistry"/>.</summary>
public static class PermissionCatalogue
{
    private static readonly ScopeType[] Org = [ScopeType.Organisation];
    private static readonly ScopeType[] Team = [ScopeType.Team];
    private static readonly ScopeType[] Project = [ScopeType.Project];
    private static readonly ScopeType[] OrgOrProject = [ScopeType.Organisation, ScopeType.Project];

    private static readonly PermissionDescriptor[] CoreDescriptors =
    [
        new(OrganisationPermissions.OrganisationRead, "Organisation", "Read organisation", "View organisation details, settings, and projects.", Org),
        new(OrganisationPermissions.OrganisationManage, "Organisation", "Manage organisation", "Rename the organisation and change organisation-wide settings.", Org),
        new(OrganisationPermissions.OrganisationSettingsManage, "Organisation", "Manage organisation settings", "Change organisation settings.", Org),
        new(OrganisationPermissions.OrganisationDestroy, "Organisation", "Destroy organisation", "Permanently delete the organisation and all of its data.", Org),
        new(OrganisationPermissions.UsersRead, "People", "Read people", "View members, teams, and their assignments.", Org),
        new(OrganisationPermissions.UsersInvite, "People", "Invite people", "Invite new organisation members.", Org),
        new(OrganisationPermissions.UsersManage, "People", "Manage people", "Invite, suspend, remove members, and change organisation roles.", Org),
        new(OrganisationPermissions.TeamsRead, "People", "Read teams", "View all teams in the organisation.", Org),
        new(OrganisationPermissions.TeamsCreate, "People", "Create teams", "Create new teams.", Org),
        new(OrganisationPermissions.TeamsManage, "People", "Manage teams", "Create teams, edit membership, and attach access roles.", Org),
        new(OrganisationPermissions.TeamsReadAssigned, "People", "Read assigned teams", "View teams the user belongs to.", Org),
        new(OrganisationPermissions.ProjectsCreate, "Projects", "Create projects", "Create new projects in the organisation.", Org),
        new(OrganisationPermissions.ProjectsManage, "Projects", "Manage projects", "Edit project settings, membership, and repositories.", Org),
        new(OrganisationPermissions.ProjectsRead, "Projects", "Read projects", "View all projects in the organisation.", Org),
        new(OrganisationPermissions.ProjectsReadAccessible, "Projects", "Read accessible projects", "View projects the user can access.", Org),
        new(OrganisationPermissions.IntegrationsManage, "Platform", "Manage integrations", "Configure provider credentials and outbound integrations.", Org),
        new(OrganisationPermissions.ModulesManage, "Platform", "Manage modules", "Install, enable, and disable extensions and modules.", Org),
        new(OrganisationPermissions.ExtensionsRead, "Platform", "Read extensions", "View installed extensions and modules.", Org),
        new(OrganisationPermissions.ExtensionsInstall, "Platform", "Install extensions", "Install extension packages.", Org),
        new(OrganisationPermissions.ExtensionsEnable, "Platform", "Enable extensions", "Enable installed extensions.", Org),
        new(OrganisationPermissions.ExtensionsDisable, "Platform", "Disable extensions", "Disable installed extensions.", Org),
        new(OrganisationPermissions.ExtensionsUninstall, "Platform", "Uninstall extensions", "Uninstall extension packages.", Org),
        new(OrganisationPermissions.ConnectorsRead, "Platform", "Read connectors", "View connector configuration.", Org),
        new(OrganisationPermissions.ConnectorsManage, "Platform", "Manage connectors", "Configure connectors.", Org),
        new(OrganisationPermissions.AuditRead, "Platform", "Read audit log", "Read the organisation audit trail.", Org),
        new(OrganisationPermissions.LicensingRead, "Platform", "Read licensing", "View licence status.", Org),
        new(OrganisationPermissions.LicensingManage, "Platform", "Manage licensing", "Install, replace, and remove licences.", Org),
        new(OrganisationPermissions.RolesManage, "Platform", "Manage roles", "Create and edit organisation roles.", Org),
        new(OrganisationPermissions.PermissionsManage, "Platform", "Manage permissions", "Grant and revoke direct permissions.", Org),

        new(OrganisationPermissions.TeamRead, "Team", "View team", "View team details and membership.", Team),
        new(OrganisationPermissions.TeamSettingsManage, "Team", "Manage team settings", "Change team name, description, and settings.", Team),
        new(OrganisationPermissions.TeamMembersRead, "Team", "View team members", "View team membership.", Team),
        new(OrganisationPermissions.TeamMembersManage, "Team", "Manage team members", "Add and remove team members.", Team),
        new(OrganisationPermissions.TeamRolesRead, "Team", "View team roles", "View team-scoped roles.", Team),
        new(OrganisationPermissions.TeamRolesManage, "Team", "Manage team roles", "Create and assign team-scoped roles.", Team),
        new(OrganisationPermissions.TeamProjectsRead, "Team", "View team projects", "View projects owned or accessed by the team.", Team),
        new(OrganisationPermissions.TeamProjectsCreate, "Team", "Create team projects", "Create projects owned by the team.", Team),
        new(OrganisationPermissions.TeamProjectsManage, "Team", "Manage team projects", "Manage projects owned by the team.", Team),

        new(OrganisationPermissions.ProjectRead, "Project", "View project", "View project overview and settings.", Project),
        new(OrganisationPermissions.ProjectSettingsManage, "Project", "Manage project settings", "Change project settings.", Project),
        new(OrganisationPermissions.ProjectMembersManage, "Project", "Manage project members", "Grant and revoke project access.", Project),
        new(OrganisationPermissions.ProjectPermissionsManage, "Project", "Manage project permissions", "Grant direct project permissions and edit project roles.", Project),

        new("source.repository.read", "Source", "Read repositories", "Browse connected source repositories.", OrgOrProject, "forgedeck.source"),
        new("source.repository.connect", "Source", "Connect repositories", "Connect and disconnect external source repositories.", OrgOrProject, "forgedeck.source"),

        new("review.read", "Review", "Read reviews", "View pull requests, discussions, and review state.", Project, "forgedeck.review"),
        new("review.comment", "Review", "Comment on reviews", "Add comments and review notes to pull requests.", Project, "forgedeck.review"),
        new("review.request", "Review", "Request review", "Request review from members and teams.", Project, "forgedeck.review"),
        new("review.approve", "Review", "Approve reviews", "Approve or request changes on a pull request.", Project, "forgedeck.review"),
        new("review.merge", "Review", "Merge reviews", "Merge approved pull requests.", Project, "forgedeck.review"),
        new("review.manage", "Review", "Manage review policy", "Change review policies, gates, and required approvals.", Project, "forgedeck.review"),

        new("git.repository.read", "Git", "Read Git repositories", "Clone and fetch hosted Git repositories.", Project, "forgedeck.git"),
        new("git.repository.create", "Git", "Create Git repositories", "Create hosted Git repositories.", Project, "forgedeck.git"),
        new("git.repository.push", "Git", "Push to Git repositories", "Push commits and branches to hosted Git repositories.", Project, "forgedeck.git"),

        new("pipelines.read", "Build", "Read pipelines", "View pipelines, runs, and job logs.", Project, "forgedeck.build"),
        new("pipelines.run", "Build", "Run pipelines", "Queue pipeline runs.", Project, "forgedeck.build"),
        new("pipelines.cancel", "Build", "Cancel pipeline runs", "Cancel queued or running pipeline runs.", Project, "forgedeck.build"),
        new("pipelines.manage", "Build", "Manage pipelines", "Create, edit, and delete pipeline definitions.", Project, "forgedeck.build"),
        new("pipelines.runner.read", "Build", "Read runners", "View registered build runners and their status.", OrgOrProject, "forgedeck.build"),
        new("pipelines.runner.manage", "Build", "Manage runners", "Register and revoke build runners.", OrgOrProject, "forgedeck.build"),

        new("deploy.read", "Deploy", "Read deployments", "View environments, releases, and deployment history.", Project, "forgedeck.deploy"),
        new("deploy.execute", "Deploy", "Execute deployments", "Trigger deployments to environments.", Project, "forgedeck.deploy"),
        new("deploy.approve", "Deploy", "Approve deployments", "Approve gated deployments.", Project, "forgedeck.deploy"),
        new("deploy.manage", "Deploy", "Manage deployments", "Create and edit environments, gates, and deployment policy.", Project, "forgedeck.deploy")
    ];

    private static readonly Dictionary<string, PermissionDescriptor> ByKey =
        CoreDescriptors.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<PermissionDescriptor> All => CoreDescriptors;

    public static IReadOnlySet<string> AllKeys { get; } = new HashSet<string>(CoreDescriptors.Select(d => d.Key), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Categories =>
        CoreDescriptors.Select(d => d.Category).Distinct(StringComparer.Ordinal).ToArray();

    public static PermissionDescriptor? Find(string? permission) =>
        !string.IsNullOrWhiteSpace(permission) && ByKey.TryGetValue(permission.Trim(), out var descriptor)
            ? descriptor
            : null;

    public static bool IsKnown(string? permission) => Find(permission) is not null;

    public static bool AllowsScope(string permission, ScopeType scope) =>
        Find(permission) is { } descriptor && descriptor.AllowedScopes.Contains(scope);

    /// <summary>Trims, de-duplicates, and rejects unknown permission keys.</summary>
    public static HashSet<string> Normalise(IEnumerable<string>? permissions, IPermissionDefinitionRegistry? registry = null)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (permissions is null)
        {
            return result;
        }

        foreach (var raw in permissions)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var candidate = raw.Trim();
            if (ByKey.TryGetValue(candidate, out var core))
            {
                result.Add(core.Key);
                continue;
            }

            if (registry?.Find(candidate) is { } registered)
            {
                result.Add(registered.Key);
                continue;
            }

            throw new ArgumentException($"Unknown permission '{candidate}'.");
        }

        return result;
    }
}
