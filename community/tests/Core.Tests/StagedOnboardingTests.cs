using ForgeDeck.Core.Application;
using ForgeDeck.Core.Domain;

namespace Core.Tests;

public sealed class StagedOnboardingTests
{
    [Fact]
    public void Staged_flow_organisation_licence_owner_without_modules()
    {
        using var fixture = new TenancyFixture();
        var token = fixture.Setup.BootstrapLogin("admin", "admin");
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(fixture.Setup.IsBootstrapTokenValid(token));

        var org = fixture.Setup.SaveOrganisation(new OrganisationSetupRequest("Northstar Labs", "Engineering", null));
        Assert.Equal("Northstar Labs", org.Name);
        Assert.False(fixture.Setup.GetStatus().HasLicence);

        // Without LicenceService wired, community selection is unavailable in this fixture.
        // Simulate community selection the same way the store/licence path does after setup.
        var instance = fixture.Store.GetInstance();
        instance.LicenceMode = LicenceMode.Community;
        fixture.Store.SaveInstance(instance);
        fixture.Store.ReplaceActiveLicence(new LicenceRecord
        {
            Mode = LicenceMode.Community,
            Status = LicenceRecordStatus.Active,
            LicenceId = "community"
        });

        var statusAfterLicence = fixture.Setup.GetStatus();
        Assert.DoesNotContain(statusAfterLicence.Steps, s => s.Id == "modules");
        Assert.Contains(statusAfterLicence.Steps, s => s.Id == "owner" && s.Current);

        var owner = fixture.Setup.CreateOwner(new OwnerSetupRequest("Rowan Smith", "rowan", "rowan@example.com", "password123"));
        Assert.Equal(OrganisationRole.Owner, fixture.Store.GetMembership(owner.Id)!.Role);
        Assert.Equal(InstanceState.Initialised, fixture.Store.GetInstance().State);
        Assert.False(fixture.Store.GetInstance().BootstrapEnabled);
        Assert.False(fixture.Setup.IsBootstrapTokenValid(token));
        Assert.Throws<InvalidOperationException>(() => fixture.Setup.BootstrapLogin("admin", "admin"));

        var afterOwner = fixture.Setup.GetStatus();
        Assert.False(afterOwner.SetupCompleted);
        Assert.False(afterOwner.HasMembers);
        Assert.False(afterOwner.HasFirstProject);
        Assert.Contains(afterOwner.Steps, s => s.Id == "members" && s.Current);
        Assert.Contains(afterOwner.Steps, s => s.Id == "project");

        fixture.Setup.AcknowledgeMembers();
        Assert.True(fixture.Setup.GetStatus().HasMembers);
        Assert.Contains(fixture.Setup.GetStatus().Steps, s => s.Id == "project" && s.Current);

        fixture.Setup.AcknowledgeProject();
        Assert.True(fixture.Setup.GetStatus().HasFirstProject);
        Assert.False(fixture.Setup.GetStatus().HasProjects);

        fixture.Setup.MarkSetupCompleted();
        Assert.True(fixture.Setup.GetStatus().SetupCompleted);
    }

    [Fact]
    public void Project_modules_are_always_available_when_organisation_enabled()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var project = fixture.Projects.CreateProject(
            new CreateProjectRequest("Atlas", "atlas", null, null),
            owner.Id);
        Assert.True(fixture.Store.IsProjectModuleEnabled(project.Id, "forgedeck.review"));

        // Legacy per-project enablement is ignored.
        fixture.Projects.SetEnabledModules(project.Id, ["forgedeck.code"]);
        Assert.True(fixture.Store.IsProjectModuleEnabled(project.Id, "forgedeck.review"));
        Assert.True(fixture.Store.IsProjectModuleEnabled(project.Id, "forgedeck.code"));
    }

    [Fact]
    public void Single_repository_mode_rejects_downgrade_with_multiple_repos()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var project = fixture.Projects.CreateProject(
            new CreateProjectRequest("Atlas", "atlas", null, null, ProjectVisibility.Private, RepositoryMode.MultiRepository),
            owner.Id);
        fixture.Store.SaveRepository(new Repository { ProjectId = project.Id, Name = "atlas", Slug = "atlas" });
        fixture.Store.SaveRepository(new Repository { ProjectId = project.Id, Name = "atlas-web", Slug = "atlas-web" });

        var error = Assert.Throws<InvalidOperationException>(() =>
            fixture.Projects.SetRepositoryMode(project.Id, RepositoryMode.SingleRepository));
        Assert.Contains("multiple repositories", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Instance_id_is_stable_once_assigned()
    {
        using var fixture = new TenancyFixture();
        fixture.Store.EnsureInstanceId();
        var first = fixture.Store.GetInstance().InstanceId;
        fixture.Store.EnsureInstanceId();
        Assert.Equal(first, fixture.Store.GetInstance().InstanceId);
        Assert.NotEqual(Guid.Empty, first);
    }
}
