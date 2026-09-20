using Platform.Core.Domain;
using Platform.Core.Identity;

namespace Platform.Core.Context;

public sealed class PlatformContextStore
{
    private static readonly IReadOnlySet<string> BootstrapPermissions = OrganisationPermissions.ForRole(OrganisationRole.Owner);

    public OrganisationView Organisation { get; private set; }
    public ProjectView Project { get; private set; }
    public PlatformUser User { get; private set; }

    public PlatformContextStore()
    {
        Organisation = new(KnownIds.OrganisationId, "Northstar Labs", "northstar");
        Project = new(KnownIds.AtlasProjectId, KnownIds.OrganisationId, "Atlas", "ATL", "github.com/northstar/atlas");
        User = new(KnownIds.MayaUserId, "Maya Chen", "maya@northstar.dev", BootstrapPermissions);
    }

    public PlatformContextStore(Platform.Core.Persistence.ITenancyStore store) : this()
    {
        if (store.GetInstance().State != InstanceState.Initialised) return;
        var organisation = store.GetOrganisation();
        var project = store.ListProjects().FirstOrDefault();
        var membership = store.ListMemberships().FirstOrDefault(m => m.Status == MembershipStatus.Active);
        var user = membership is null ? null : store.FindUser(membership.UserId);
        if (organisation is null || project is null || user is null || membership is null) return;
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
        new(user.Id, profile?.DisplayName ?? user.Username, user.Email, OrganisationPermissions.ForRole(role));
}
