using Platform.Core.Application;
using Platform.Core.Domain;

namespace Tests.Unit;

public sealed class BootstrapAndOwnerTests
{
    [Fact]
    public void Bootstrap_creates_organisation_owner_profile_and_initialises_instance()
    {
        using var fixture = new TenancyFixture();

        var user = fixture.Bootstrap();

        Assert.Equal(InstanceState.Initialised, fixture.Store.GetInstance().State);
        Assert.Equal(KnownIds.OrganisationId, fixture.Store.GetOrganisation()!.Id);
        Assert.Equal("Northstar Labs", fixture.Store.GetOrganisation()!.Name);
        Assert.Equal(OrganisationRole.Owner, fixture.Store.GetMembership(user.Id)!.Role);
        Assert.Equal(MembershipStatus.Active, fixture.Store.GetMembership(user.Id)!.Status);
        Assert.Equal("Maya Chen", fixture.Store.GetProfile(user.Id)!.DisplayName);
        Assert.True(fixture.Setup.GetStatus().Initialised);
        Assert.False(fixture.Setup.GetStatus().HasProjects);
    }

    [Fact]
    public void Second_bootstrap_is_rejected()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Bootstrap("other@example.com", "other", "Other"));
        Assert.Contains("already been initialised", error.Message);
        Assert.Single(fixture.Store.ListMemberships());
    }

    [Fact]
    public void Bootstrap_rejects_invalid_username_and_short_password()
    {
        using var fixture = new TenancyFixture();

        Assert.Throws<ArgumentException>(() =>
            fixture.Setup.Bootstrap(new SetupRequest("Org", null, "Name", "-bad", "a@b.co", "password123")));
        Assert.Throws<ArgumentException>(() =>
            fixture.Setup.Bootstrap(new SetupRequest("Org", null, "Name", "ok", "a@b.co", "short")));
        Assert.Equal(InstanceState.Uninitialised, fixture.Store.GetInstance().State);
    }

    [Fact]
    public void Last_owner_cannot_be_demoted_suspended_or_removed()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();

        Assert.Throws<InvalidOperationException>(() => fixture.Memberships.ChangeRole(owner.Id, OrganisationRole.Member));
        Assert.Throws<InvalidOperationException>(() => fixture.Memberships.ChangeStatus(owner.Id, MembershipStatus.Suspended));
        Assert.Throws<InvalidOperationException>(() => fixture.Memberships.Remove(owner.Id));
    }

    [Fact]
    public void Second_owner_allows_self_demotion()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var alice = fixture.Memberships.Add("alice@example.com", "alice", "Alice", "password123", OrganisationRole.Owner, owner.Id);

        fixture.Memberships.ChangeRole(owner.Id, OrganisationRole.Admin);

        Assert.Equal(OrganisationRole.Admin, fixture.Store.GetMembership(owner.Id)!.Role);
        Assert.Equal(OrganisationRole.Owner, fixture.Store.GetMembership(alice.Id)!.Role);
    }

    [Fact]
    public void Suspend_and_remove_update_user_status_and_strip_access()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        var team = fixture.Teams.Create("Backend", "backend", null);
        fixture.Teams.AddMember(team.Id, bob.Id);
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Secret", "secret", null, null), owner.Id);
        fixture.Projects.GrantUser(project.Id, bob.Id);

        fixture.Memberships.ChangeStatus(bob.Id, MembershipStatus.Suspended);
        Assert.Equal(UserStatus.Suspended, fixture.Store.FindUser(bob.Id)!.Status);
        Assert.Null(fixture.Auth.Login("bob@example.com", "password123"));

        fixture.Memberships.ChangeStatus(bob.Id, MembershipStatus.Active);
        fixture.Memberships.Remove(bob.Id);
        Assert.Equal(UserStatus.Disabled, fixture.Store.FindUser(bob.Id)!.Status);
        Assert.Null(fixture.Store.GetMembership(bob.Id));
        Assert.Empty(fixture.Store.ListUserTeamMemberships(bob.Id));
        Assert.False(fixture.Store.HasProjectUserAccess(project.Id, bob.Id));
    }

    [Fact]
    public void Auth_login_logout_and_session_revocation()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();

        var login = fixture.Auth.Login("maya@northstar.dev", "password123");
        Assert.NotNull(login);
        Assert.NotNull(fixture.Auth.GetUserBySessionToken(login.Token));

        fixture.Auth.Logout(login.Token);
        Assert.Null(fixture.Auth.GetUserBySessionToken(login.Token));
    }

    [Fact]
    public void Auth_rejects_wrong_password_and_suspended_user()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        Assert.Null(fixture.Auth.Login("maya@northstar.dev", "wrong-password"));

        fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Owner, owner.Id);
        fixture.Memberships.ChangeStatus(owner.Id, MembershipStatus.Suspended);
        Assert.Null(fixture.Auth.Login("maya@northstar.dev", "password123"));
    }
}
