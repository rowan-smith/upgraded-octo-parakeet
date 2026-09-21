using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Platform.Core.Context;
using Platform.Core.Domain;
using Platform.Core.Persistence;

namespace Platform.Core.Application;

public sealed class DevelopmentSeedService(
    IConfiguration configuration,
    ITenancyStore store,
    IPasswordHasher<UserAccount> passwords,
    ProjectService projects,
    PlatformContextStore context)
{
    public void SeedIfEnabled(bool isDevelopment)
    {
        if (!isDevelopment || !configuration.GetValue("Core:SeedDemoOnEmpty", true) ||
            store.GetInstance().State != InstanceState.Uninitialised) return;

        var now = DateTimeOffset.UtcNow;
        var organisation = new Organisation { Id = KnownIds.OrganisationId, Name = "Northstar Labs", CreatedAt = now, UpdatedAt = now };
        var user = new UserAccount
        {
            Id = KnownIds.MayaUserId,
            Email = "maya@northstar.dev",
            Username = "maya",
            PasswordHash = "",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwords.HashPassword(user, "demo");
        var profile = new UserProfile { UserId = user.Id, DisplayName = "Maya Chen", DefaultProjectId = KnownIds.AtlasProjectId };
        store.Bootstrap(organisation, user, profile,
            new OrganisationMembership { UserId = user.Id, Role = OrganisationRole.Owner, Status = MembershipStatus.Active, JoinedAt = now });
        var instance = store.GetInstance();
        instance.LicenceMode = LicenceMode.Community;
        instance.BootstrapEnabled = false;
        store.SaveInstance(instance);
        store.ReplaceActiveLicence(new LicenceRecord
        {
            Mode = LicenceMode.Community,
            Status = LicenceRecordStatus.Active,
            LicenceId = "community"
        });
        var project = projects.CreateProject(new CreateProjectRequest("Atlas", "atlas", "ATL", null, ProjectVisibility.Organisation),
            user.Id, KnownIds.AtlasProjectId);
        context.Apply(
            new OrganisationView(organisation.Id, organisation.Name, "northstar"),
            new ProjectView(project.Id, organisation.Id, project.Name, project.Key, null),
            PlatformContextStore.ToPlatformUser(user, profile, OrganisationRole.Owner));
    }
}
