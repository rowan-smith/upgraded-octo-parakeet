using ForgeDeck.Core.Domain;

namespace ForgeDeck.Core.Identity;

/// <summary>Seed definition for a built-in access role.</summary>
public sealed record SystemAccessRoleDefinition(string Slug, string Name, string Description, IReadOnlySet<string> Permissions);

/// <summary>
/// Built-in access roles seeded on first use. Owner/Admin/Member mirror <see cref="OrganisationRole"/> so the
/// organisation ceiling and the role catalogue never disagree; the remaining roles exist to narrow access on a
/// project or team grant.
/// </summary>
public static class SystemAccessRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";
    public const string Reader = "reader";
    public const string Developer = "developer";
    public const string Reviewer = "reviewer";
    public const string Builder = "builder";
    public const string Deployer = "deployer";

    private static readonly HashSet<string> ReaderPermissions = Set(
    [
        OrganisationPermissions.OrganisationRead,
        OrganisationPermissions.UsersRead,
        "source.repository.read",
        "review.read",
        "git.repository.read",
        "pipelines.read",
        "deploy.read"
    ]);

    private static readonly HashSet<string> DeveloperPermissions = Set(
        OrganisationPermissions.ForRole(OrganisationRole.Member).Except(["deploy.execute", "deploy.manage"], StringComparer.OrdinalIgnoreCase));

    private static readonly HashSet<string> ReviewerPermissions = Set(
        [..ReaderPermissions, "review.comment", "review.request", "review.approve"]);

    private static readonly HashSet<string> BuilderPermissions = Set(
        [..ReaderPermissions, "pipelines.read", "pipelines.run", "pipelines.manage", "pipelines.cancel", "pipelines.runner.read"]);

    private static readonly HashSet<string> DeployerPermissions = Set(
        [..ReaderPermissions, "deploy.read", "deploy.execute", "deploy.manage"]);

    private static readonly SystemAccessRoleDefinition[] DefinitionList =
    [
        new(Owner, "Owner", "Full organisation control including licensing, modules, and destructive actions.", OrganisationPermissions.ForRole(OrganisationRole.Owner)),
        new(Admin, "Admin", "Manage projects, people, teams, and integrations.", OrganisationPermissions.ForRole(OrganisationRole.Admin)),
        new(Member, "Member", "Collaborate across source, review, build, and deploy on granted projects.", OrganisationPermissions.ForRole(OrganisationRole.Member)),
        new(Reader, "Reader", "Read-only visibility across source, review, build, and deploy.", ReaderPermissions),
        new(Developer, "Developer", "Member access without the ability to execute or manage deployments.", DeveloperPermissions),
        new(Reviewer, "Reviewer", "Read-only plus comment, request, and approve on pull requests.", ReviewerPermissions),
        new(Builder, "Builder", "Read-only plus full control of pipelines and visibility of runners.", BuilderPermissions),
        new(Deployer, "Deployer", "Read-only plus execute and manage deployments.", DeployerPermissions)
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
