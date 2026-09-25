using ForgeDeck.Core.Context;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Extensions;
using ForgeDeck.Core.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace ForgeDeck.Core.Application;

/// <summary>
/// Development dogfood seeder: boots a ForgeDeck organisation working on the ForgeDeck product itself.
/// </summary>
public sealed class DevelopmentSeedService(
    IConfiguration configuration,
    ITenancyStore store,
    IPasswordHasher<UserAccount> passwords,
    ProjectService projects,
    PlatformContextStore context,
    ExtensionLifecycleService? extensions = null)
{
    public void SeedIfEnabled()
    {
        if (!configuration.GetValue("Core:SeedDemoOnEmpty", false) ||
            store.GetInstance().State != InstanceState.Uninitialised)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var organisation = new Organisation
        {
            Id = KnownIds.OrganisationId,
            Name = "ForgeDeck",
            Description = "Dogfood — building ForgeDeck on ForgeDeck",
            CreatedAt = now,
            UpdatedAt = now
        };
        var user = new UserAccount
        {
            Id = KnownIds.MayaUserId,
            Email = "maya@forgedeck.dev",
            Username = "maya",
            PasswordHash = "",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwords.HashPassword(user, "demo");
        var profile = new UserProfile
        {
            UserId = user.Id,
            DisplayName = "Maya Chen",
            DefaultProjectId = KnownIds.AtlasProjectId,
            Theme = "system"
        };
        store.Bootstrap(organisation, user, profile,
            new OrganisationMembership { UserId = user.Id, Role = OrganisationRole.Owner, Status = MembershipStatus.Active, JoinedAt = now });

        var instance = store.GetInstance();
        instance.LicenceMode = LicenceMode.Community;
        instance.BootstrapEnabled = false;
        instance.ModulesAcknowledgedAt = now;
        store.SaveInstance(instance);
        store.ReplaceActiveLicence(new LicenceRecord
        {
            Mode = LicenceMode.Community,
            Status = LicenceRecordStatus.Active,
            LicenceId = "community"
        });

        var project = projects.CreateProject(
            new CreateProjectRequest(
                "ForgeDeck",
                "forgedeck",
                "FD",
                "The ForgeDeck product repository — dogfood deployment.",
                ProjectVisibility.Organisation,
                RepositoryMode.SingleRepository),
            user.Id,
            KnownIds.AtlasProjectId);

        context.Apply(
            new OrganisationView(organisation.Id, organisation.Name, "forgedeck"),
            new ProjectView(project.Id, organisation.Id, project.Name, project.Key, null),
            PlatformContextStore.ToPlatformUser(user, profile, OrganisationRole.Owner));

        extensions?.SeedDogfoodDefaults(user.Email, configuration);

        // Enable installed modules for the dogfood project explicitly.
        if (extensions is not null)
        {
            var enabled = extensions.List(Contracts.Extensions.ExtensionType.Module)
                .Where(m => m.Installed && m.Enabled)
                .Select(m => m.ExtensionId)
                .ToArray();
            if (enabled.Length > 0)
            {
                projects.SetEnabledModules(project.Id, enabled);
            }
        }
    }
}
