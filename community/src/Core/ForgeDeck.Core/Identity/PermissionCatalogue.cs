namespace ForgeDeck.Core.Identity;

/// <summary>One entry per grantable permission. Drives role editors and validation of custom roles.</summary>
public sealed record PermissionDescriptor(string Key, string Category, string Title, string Description);

/// <summary>The complete set of organisation and module permissions a role may contain.</summary>
public static class PermissionCatalogue
{
    private static readonly PermissionDescriptor[] Descriptors =
    [
        new(OrganisationPermissions.OrganisationRead, "Organisation", "Read organisation", "View organisation details, settings, and projects."),
        new(OrganisationPermissions.OrganisationManage, "Organisation", "Manage organisation", "Rename the organisation and change organisation-wide settings."),
        new(OrganisationPermissions.OrganisationDestroy, "Organisation", "Destroy organisation", "Permanently delete the organisation and all of its data."),
        new(OrganisationPermissions.UsersRead, "People", "Read people", "View members, teams, and their assignments."),
        new(OrganisationPermissions.UsersManage, "People", "Manage people", "Invite, suspend, remove members, and change organisation roles."),
        new(OrganisationPermissions.TeamsManage, "People", "Manage teams", "Create teams, edit membership, and attach access roles."),
        new(OrganisationPermissions.ProjectsCreate, "Projects", "Create projects", "Create new projects in the organisation."),
        new(OrganisationPermissions.ProjectsManage, "Projects", "Manage projects", "Edit project settings, membership, and repositories."),
        new(OrganisationPermissions.IntegrationsManage, "Platform", "Manage integrations", "Configure provider credentials and outbound integrations."),
        new(OrganisationPermissions.ModulesManage, "Platform", "Manage modules", "Install, enable, and disable extensions and modules."),
        new(OrganisationPermissions.AuditRead, "Platform", "Read audit log", "Read the organisation audit trail."),
        new(OrganisationPermissions.LicensingManage, "Platform", "Manage licensing", "Install, replace, and remove licences."),

        new("source.repository.read", "Source", "Read repositories", "Browse connected source repositories."),
        new("source.repository.connect", "Source", "Connect repositories", "Connect and disconnect external source repositories."),

        new("review.read", "Review", "Read reviews", "View pull requests, discussions, and review state."),
        new("review.comment", "Review", "Comment on reviews", "Add comments and review notes to pull requests."),
        new("review.request", "Review", "Request review", "Request review from members and teams."),
        new("review.approve", "Review", "Approve reviews", "Approve or request changes on a pull request."),
        new("review.merge", "Review", "Merge reviews", "Merge approved pull requests."),
        new("review.manage", "Review", "Manage review policy", "Change review policies, gates, and required approvals."),

        new("git.repository.read", "Git", "Read Git repositories", "Clone and fetch hosted Git repositories."),
        new("git.repository.create", "Git", "Create Git repositories", "Create hosted Git repositories."),
        new("git.repository.push", "Git", "Push to Git repositories", "Push commits and branches to hosted Git repositories."),

        new("pipelines.read", "Build", "Read pipelines", "View pipelines, runs, and job logs."),
        new("pipelines.run", "Build", "Run pipelines", "Queue pipeline runs."),
        new("pipelines.cancel", "Build", "Cancel pipeline runs", "Cancel queued or running pipeline runs."),
        new("pipelines.manage", "Build", "Manage pipelines", "Create, edit, and delete pipeline definitions."),
        new("pipelines.runner.read", "Build", "Read runners", "View registered build runners and their status."),
        new("pipelines.runner.manage", "Build", "Manage runners", "Register and revoke build runners."),

        new("deploy.read", "Deploy", "Read deployments", "View environments, releases, and deployment history."),
        new("deploy.execute", "Deploy", "Execute deployments", "Trigger deployments to environments."),
        new("deploy.manage", "Deploy", "Manage deployments", "Create and edit environments, gates, and deployment policy.")
    ];

    private static readonly HashSet<string> Keys = new(Descriptors.Select(d => d.Key), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<PermissionDescriptor> All => Descriptors;

    public static IReadOnlySet<string> AllKeys => Keys;

    public static IReadOnlyList<string> Categories =>
        Descriptors.Select(d => d.Category).Distinct(StringComparer.Ordinal).ToArray();

    public static bool IsKnown(string? permission) =>
        !string.IsNullOrWhiteSpace(permission) && Keys.Contains(permission.Trim());

    /// <summary>Trims, de-duplicates, and rejects unknown permission keys.</summary>
    public static HashSet<string> Normalise(IEnumerable<string>? permissions)
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
            if (!Keys.TryGetValue(candidate, out var canonical))
            {
                throw new ArgumentException($"Unknown permission '{candidate}'.");
            }

            result.Add(canonical);
        }

        return result;
    }
}
