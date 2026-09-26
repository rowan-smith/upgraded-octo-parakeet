using ForgeDeck.Core.Domain;

namespace ForgeDeck.Core.Identity;

/// <summary>Maps organisation roles to permission bundles. Authorization stays permission-based.</summary>
public static class OrganisationPermissions
{
    public const string OrganisationRead = "organisation.read";
    public const string OrganisationManage = "organisation.manage";
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string TeamsManage = "teams.manage";
    public const string ProjectsCreate = "projects.create";
    public const string ProjectsManage = "projects.manage";
    public const string IntegrationsManage = "core.integration.manage";
    public const string ModulesManage = "modules.manage";
    public const string AuditRead = "audit.read";
    public const string LicensingManage = "licensing.manage";
    public const string OrganisationDestroy = "organisation.destroy";

    private static readonly HashSet<string> ModulePermissions = Set(
    [
        "source.repository.read", "source.repository.connect",
        "review.read", "review.comment", "review.request", "review.approve", "review.merge", "review.manage",
        "git.repository.create", "git.repository.push", "git.repository.read",
        "pipelines.manage", "pipelines.run", "pipelines.read", "pipelines.cancel",
        "pipelines.runner.read", "pipelines.runner.manage",
        "deploy.read", "deploy.execute", "deploy.manage"
    ]);

    private static readonly HashSet<string> MemberBase = Set(
    [
        OrganisationRead, UsersRead, ..ModulePermissions
    ]);

    private static readonly HashSet<string> AdminBase = Set(
    [
        ..MemberBase,
        UsersManage, TeamsManage, ProjectsCreate, ProjectsManage, IntegrationsManage, AuditRead
    ]);

    private static readonly HashSet<string> OwnerBase = Set(
    [
        ..AdminBase,
        OrganisationManage, ModulesManage, LicensingManage, OrganisationDestroy
    ]);

    /// <summary>Permission comparisons are case-insensitive everywhere so custom roles cannot smuggle casing past a check.</summary>
    private static HashSet<string> Set(IEnumerable<string> permissions) => new(permissions, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> ForRole(OrganisationRole role) => role switch
    {
        OrganisationRole.Owner => OwnerBase,
        OrganisationRole.Admin => AdminBase,
        _ => MemberBase
    };
}
