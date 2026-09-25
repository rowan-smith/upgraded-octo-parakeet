using ForgeDeck.Core.Domain;

namespace Core.Tests;

public sealed class TeamServiceTests
{
    [Fact]
    public void Create_edit_add_remove_and_delete_team()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);

        var team = fixture.Teams.Create("Backend", "backend", "API team");
        Assert.Equal("backend", team.Slug);
        Assert.Single(fixture.Teams.List());

        fixture.Teams.AddMember(team.Id, bob.Id);
        Assert.Single(fixture.Teams.ListMembers(team.Id));

        fixture.Teams.Update(team.Id, "Platform Backend", "platform-backend", "Updated");
        Assert.Equal("platform-backend", fixture.Teams.Get(team.Id)!.Slug);

        fixture.Teams.RemoveMember(team.Id, bob.Id);
        Assert.Empty(fixture.Teams.ListMembers(team.Id));

        fixture.Teams.Delete(team.Id);
        Assert.Empty(fixture.Teams.List());
    }

    [Fact]
    public void Team_slug_must_be_unique_and_valid()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        fixture.Teams.Create("Backend", "backend", null);

        Assert.Throws<ArgumentException>(() => fixture.Teams.Create("Other", "backend", null));
        Assert.Throws<ArgumentException>(() => fixture.Teams.Create("Bad", "-x", null));
    }

    [Fact]
    public void Suspended_member_cannot_join_team()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var bob = fixture.Memberships.Add("bob@example.com", "bob", "Bob", "password123", OrganisationRole.Member, owner.Id);
        fixture.Memberships.ChangeStatus(bob.Id, MembershipStatus.Suspended);
        var team = fixture.Teams.Create("Backend", "backend", null);

        Assert.Throws<InvalidOperationException>(() => fixture.Teams.AddMember(team.Id, bob.Id));
    }
}
