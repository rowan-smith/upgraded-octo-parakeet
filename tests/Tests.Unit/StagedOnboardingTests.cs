using Microsoft.Extensions.Options;
using Platform.Core.Application;
using Platform.Core.Domain;
using Platform.Core.Identity;

namespace Tests.Unit;

public sealed class StagedOnboardingTests
{
    [Fact]
    public void Staged_flow_organisation_community_owner()
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

        var owner = fixture.Setup.CreateOwner(new OwnerSetupRequest("Rowan Smith", "rowan", "rowan@example.com", "password123"));
        Assert.Equal(OrganisationRole.Owner, fixture.Store.GetMembership(owner.Id)!.Role);
        Assert.Equal(InstanceState.Initialised, fixture.Store.GetInstance().State);
        Assert.False(fixture.Store.GetInstance().BootstrapEnabled);
        Assert.False(fixture.Setup.IsBootstrapTokenValid(token));
        Assert.Throws<InvalidOperationException>(() => fixture.Setup.BootstrapLogin("admin", "admin"));
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
