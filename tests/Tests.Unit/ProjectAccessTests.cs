using Platform.Core.Application;
using Platform.Core.Domain;

namespace Tests.Unit;

public sealed class ProjectAccessTests
{
    [Fact]
    public void Owner_and_admin_can_access_private_projects()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var admin = fixture.Memberships.Add("admin@example.com", "admin", "Admin", "password123", OrganisationRole.Admin, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Secret", "secret", null, null, ProjectVisibility.Private), owner.Id);

        Assert.True(fixture.Access.CanAccess(project, owner.Id, OrganisationRole.Owner));
        Assert.True(fixture.Access.CanAccess(project, admin.Id, OrganisationRole.Admin));
    }

    [Fact]
    public void Member_denied_private_project_without_grant()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Secret", "secret", null, null, ProjectVisibility.Private), owner.Id);

        Assert.False(fixture.Access.CanAccess(project, bob.Id, OrganisationRole.Member));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Access.EnsureAccess(project, bob.Id, OrganisationRole.Member));
    }

    [Fact]
    public void Direct_user_grant_allows_private_access()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Secret", "secret", null, null, ProjectVisibility.Private), owner.Id);
        fixture.Projects.GrantUser(project.Id, bob.Id);

        Assert.True(fixture.Access.CanAccess(project, bob.Id, OrganisationRole.Member));
        fixture.Projects.RevokeUser(project.Id, bob.Id);
        Assert.False(fixture.Access.CanAccess(project, bob.Id, OrganisationRole.Member));
    }

    [Fact]
    public void Team_grant_allows_private_access_for_team_members()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var team = fixture.Teams.Create("Backend", "backend", null);
        fixture.Teams.AddMember(team.Id, bob.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Secret", "secret", null, null, ProjectVisibility.Private), owner.Id);
        fixture.Projects.GrantTeam(project.Id, team.Id);

        Assert.True(fixture.Access.CanAccess(project, bob.Id, OrganisationRole.Member));
        fixture.Projects.RevokeTeam(project.Id, team.Id);
        Assert.False(fixture.Access.CanAccess(project, bob.Id, OrganisationRole.Member));
    }

    [Fact]
    public void Organisation_visibility_allows_all_active_members()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Shared", "shared", null, null, ProjectVisibility.Organisation), owner.Id);

        Assert.True(fixture.Access.CanAccess(project, bob.Id, OrganisationRole.Member));
    }

    [Fact]
    public void ListAccessible_filters_private_projects_for_members()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        fixture.Projects.CreateProject(new CreateProjectRequest("Secret", "secret", null, null, ProjectVisibility.Private), owner.Id);
        fixture.Projects.CreateProject(new CreateProjectRequest("Shared", "shared", null, null, ProjectVisibility.Organisation), owner.Id);

        var forBob = fixture.Projects.ListAccessible(bob.Id, OrganisationRole.Member);
        Assert.Single(forBob);
        Assert.Equal("shared", forBob[0].Slug);

        var forOwner = fixture.Projects.ListAccessible(owner.Id, OrganisationRole.Owner);
        Assert.Equal(2, forOwner.Count);
    }

    [Fact]
    public void Project_slug_must_be_unique_and_valid()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        fixture.Projects.CreateProject(new CreateProjectRequest("Platform", "platform", null, null), owner.Id);

        Assert.Throws<ArgumentException>(() =>
            fixture.Projects.CreateProject(new CreateProjectRequest("Bad", "x", null, null), owner.Id));
        Assert.ThrowsAny<Exception>(() =>
            fixture.Projects.CreateProject(new CreateProjectRequest("Other", "platform", null, null), owner.Id));
    }

    [Fact]
    public void Creator_receives_direct_project_access()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Platform", "platform", null, null), owner.Id);
        Assert.True(fixture.Store.HasProjectUserAccess(project.Id, owner.Id));
    }
}
