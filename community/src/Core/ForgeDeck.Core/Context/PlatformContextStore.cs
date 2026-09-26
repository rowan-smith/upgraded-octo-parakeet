using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Identity;
using ForgeDeck.Core.Persistence;

namespace ForgeDeck.Core.Context;

public sealed class PlatformContextStore
{
    private static readonly IReadOnlySet<string> BootstrapPermissions = OrganisationPermissions.ForRole(OrganisationRole.Owner);

    public OrganisationView Organisation { get; private set; }
    public ProjectView Project { get; private set; }
    public PlatformUser User { get; private set; }

    public PlatformContextStore()
    {
        Organisation = new(KnownIds.OrganisationId, "ForgeDeck", "forgedeck");
        Project = new(KnownIds.AtlasProjectId, KnownIds.OrganisationId, "ForgeDeck", "FD", "github.com/rowan-smith/upgraded-octo-parakeet");
        User = new(KnownIds.MayaUserId, "Maya Chen", "maya@forgedeck.dev", BootstrapPermissions);
    }

    public PlatformContextStore(ITenancyStore store) : this()
    {
        if (store.GetInstance().State != InstanceState.Initialised)
        {
            return;
        }

        var organisation = store.GetOrganisation();
        var project = store.ListProjects().FirstOrDefault();
        var membership = store.ListMemberships().FirstOrDefault(m => m.Status == MembershipStatus.Active);
        var user = membership is null ? null : store.FindUser(membership.UserId);
        if (organisation is null || project is null || user is null || membership is null)
        {
            return;
        }

        var profile = store.GetProfile(user.Id);
        var repository = store.ListRepositories(project.Id).FirstOrDefault();
        Apply(
            new OrganisationView(organisation.Id, organisation.Name, SlugRules.Normalize(organisation.Name)),
            new ProjectView(project.Id, organisation.Id, project.Name, project.Key, repository?.Name),
            ToPlatformUser(user, profile, membership.Role));
    }

    public void Apply(OrganisationView org, ProjectView project, PlatformUser user)
    {
        Organisation = org;
        Project = project;
        User = user;
    }

    public void SetProject(ProjectView project) => Project = project;
    public void SetUser(PlatformUser user) => User = user;

    public static PlatformUser ToPlatformUser(UserAccount user, UserProfile? profile, OrganisationRole role = OrganisationRole.Member) =>
        ToPlatformUser(user, profile, OrganisationPermissions.ForRole(role));

    /// <summary>Used when permissions have already been resolved (for example by EffectivePermissionService).</summary>
    public static PlatformUser ToPlatformUser(UserAccount user, UserProfile? profile, IReadOnlySet<string> permissions) =>
        new(user.Id, profile?.DisplayName ?? user.Username, user.Email, permissions);
}
