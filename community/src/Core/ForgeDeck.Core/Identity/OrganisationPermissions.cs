using ForgeDeck.Core.Domain;

namespace ForgeDeck.Core.Identity;

/// <summary>Maps organisation roles to permission bundles. Authorization stays permission-based.</summary>
public static class OrganisationPermissions
{
    public const string OrganisationRead = "organisation.read";
    public const string OrganisationManage = "organisation.manage";
    public const string OrganisationSettingsManage = "organisation.settings.manage";
    public const string UsersRead = "users.read";
    public const string UsersInvite = "users.invite";
    public const string UsersManage = "users.manage";
    public const string TeamsRead = "teams.read";
    public const string TeamsCreate = "teams.create";
    public const string TeamsManage = "teams.manage";
    public const string TeamsReadAssigned = "teams.read.assigned";
    public const string ProjectsCreate = "projects.create";
    public const string ProjectsManage = "projects.manage";
    public const string ProjectsRead = "projects.read";
    public const string ProjectsReadAccessible = "projects.read.accessible";
    public const string IntegrationsManage = "core.integration.manage";
    public const string ModulesManage = "modules.manage";
    public const string ExtensionsRead = "extensions.read";
    public const string ExtensionsInstall = "extensions.install";
    public const string ExtensionsEnable = "extensions.enable";
    public const string ExtensionsDisable = "extensions.disable";
    public const string ExtensionsUninstall = "extensions.uninstall";
    public const string ConnectorsRead = "connectors.read";
    public const string ConnectorsManage = "connectors.manage";
    public const string AuditRead = "audit.read";
    public const string LicensingRead = "licensing.read";
    public const string LicensingManage = "licensing.manage";
    public const string OrganisationDestroy = "organisation.destroy";
    public const string RolesManage = "roles.manage";
    public const string PermissionsManage = "permissions.manage";

    // Team-scoped permissions
    public const string TeamRead = "team.read";
    public const string TeamSettingsManage = "team.settings.manage";
    public const string TeamMembersRead = "team.members.read";
    public const string TeamMembersManage = "team.members.manage";
    public const string TeamRolesRead = "team.roles.read";
    public const string TeamRolesManage = "team.roles.manage";
    public const string TeamProjectsRead = "team.projects.read";
    public const string TeamProjectsCreate = "team.projects.create";
    public const string TeamProjectsManage = "team.projects.manage";

    // Project-scoped core permissions
    public const string ProjectRead = "project.read";
    public const string ProjectSettingsManage = "project.settings.manage";
    public const string ProjectMembersManage = "project.members.manage";
    public const string ProjectPermissionsManage = "project.permissions.manage";

    private static readonly HashSet<string> ModulePermissions = Set(
    [
        "source.repository.read", "source.repository.connect",
        "review.read", "review.comment", "review.request", "review.approve", "review.merge", "review.manage",
        "git.repository.create", "git.repository.push", "git.repository.read",
        "pipelines.manage", "pipelines.run", "pipelines.read", "pipelines.cancel",
        "pipelines.runner.read", "pipelines.runner.manage",
        "deploy.read", "deploy.execute", "deploy.manage", "deploy.approve"
    ]);

    private static readonly HashSet<string> MemberBase = Set(
    [
        OrganisationRead,
        UsersRead,
        TeamsReadAssigned,
        ProjectsReadAccessible,
        ProjectRead
    ]);

    private static readonly HashSet<string> AdminBase = Set(
    [
        ..MemberBase,
        UsersInvite, UsersManage,
        TeamsRead, TeamsCreate, TeamsManage,
        ProjectsRead, ProjectsCreate, ProjectsManage,
        ExtensionsRead, IntegrationsManage, ConnectorsRead, AuditRead,
        RolesManage,
        ..ModulePermissions
    ]);

    private static readonly HashSet<string> OwnerBase = Set(
    [
        ..AdminBase,
        OrganisationManage, OrganisationSettingsManage,
        ModulesManage,
        ExtensionsInstall, ExtensionsEnable, ExtensionsDisable, ExtensionsUninstall,
        ConnectorsManage,
        LicensingRead, LicensingManage,
        PermissionsManage,
        OrganisationDestroy
    ]);

    /// <summary>Permission comparisons are case-insensitive everywhere so custom roles cannot smuggle casing past a check.</summary>
    private static HashSet<string> Set(IEnumerable<string> permissions) => new(permissions, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> ModulePermissionKeys => ModulePermissions;

    public static IReadOnlySet<string> ForRole(OrganisationRole role) => role switch
    {
        OrganisationRole.Owner => OwnerBase,
        OrganisationRole.Admin => AdminBase,
        _ => MemberBase
    };
}
