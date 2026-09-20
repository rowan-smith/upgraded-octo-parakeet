using Platform.Core.Domain;

namespace Tests.Unit;

public sealed class InvitationServiceTests
{
    [Fact]
    public void Create_preview_and_accept_invitation()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var created = fixture.Invitations.Create("alice@example.com", OrganisationRole.Member, owner.Id);

        var preview = fixture.Invitations.Preview(created.RawToken);
        Assert.Equal("alice@example.com", preview.Email);
        Assert.False(preview.Accepted);
        Assert.False(preview.Expired);

        var user = fixture.Invitations.Accept(created.RawToken, "alice", "Alice", "password123");
        Assert.Equal(OrganisationRole.Member, fixture.Store.GetMembership(user.Id)!.Role);
        Assert.NotNull(fixture.Store.GetProfile(user.Id));
        Assert.NotNull(fixture.Auth.Login("alice@example.com", "password123"));
    }

    [Fact]
    public void Accept_rejects_used_and_expired_invitations()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var used = fixture.Invitations.Create("alice@example.com", OrganisationRole.Member, owner.Id);
        fixture.Invitations.Accept(used.RawToken, "alice", "Alice", "password123");
        Assert.Throws<InvalidOperationException>(() => fixture.Invitations.Accept(used.RawToken, "alice2", "Alice", "password123"));

        var expired = fixture.Invitations.Create("bob@example.com", OrganisationRole.Admin, owner.Id, TimeSpan.FromDays(-1));
        Assert.Throws<InvalidOperationException>(() => fixture.Invitations.Accept(expired.RawToken, "bob", "Bob", "password123"));
    }

    [Fact]
    public void Cannot_invite_owner_role_directly()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        Assert.Throws<ArgumentException>(() => fixture.Invitations.Create("x@example.com", OrganisationRole.Owner, owner.Id));
    }

    [Fact]
    public void Accept_existing_user_activates_membership()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        fixture.Memberships.Remove(bob.Id);

        var created = fixture.Invitations.Create("bob@example.com", OrganisationRole.Admin, owner.Id);
        var user = fixture.Invitations.Accept(created.RawToken, "bob", "Bob", "password123");

        Assert.Equal(bob.Id, user.Id);
        Assert.Equal(OrganisationRole.Admin, fixture.Store.GetMembership(user.Id)!.Role);
        Assert.Equal(UserStatus.Active, fixture.Store.FindUser(user.Id)!.Status);
    }
}
