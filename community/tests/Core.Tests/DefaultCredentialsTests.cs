using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Identity;

namespace Core.Tests;

public sealed class DefaultCredentialsTests
{
    [Fact]
    public void Seeded_default_owner_must_change_password_on_first_login()
    {
        using var fixture = new TenancyFixture();
        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount
        {
            Id = KnownIds.DefaultOwnerId,
            Email = DefaultInstallCredentials.Email,
            Username = DefaultInstallCredentials.Username,
            PasswordHash = "",
            MustChangePassword = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = fixture.Passwords.HashPassword(user, DefaultInstallCredentials.Password);
        fixture.Store.Bootstrap(
            new Organisation { Id = KnownIds.OrganisationId, Name = "ForgeDeck", CreatedAt = now, UpdatedAt = now },
            user,
            new UserProfile { UserId = user.Id, DisplayName = DefaultInstallCredentials.DisplayName },
            new OrganisationMembership { UserId = user.Id, Role = OrganisationRole.Owner, Status = MembershipStatus.Active, JoinedAt = now });

        var login = fixture.Auth.Login(DefaultInstallCredentials.Username, DefaultInstallCredentials.Password);
        Assert.NotNull(login);
        Assert.True(login.MustChangePassword);

        fixture.Auth.ChangePassword(user.Id, DefaultInstallCredentials.Password, "password123");
        var again = fixture.Auth.Login(DefaultInstallCredentials.Email, "password123");
        Assert.NotNull(again);
        Assert.False(again.MustChangePassword);
    }

    [Fact]
    public void Bootstrap_options_fill_development_defaults_when_password_blank()
    {
        var opts = new BootstrapOptions
        {
            IsDevelopmentDefault = true,
            Username = "admin",
            Password = ""
        };

        Assert.Equal(DefaultInstallCredentials.Password, opts.EffectivePassword);
        Assert.True(opts.UsesDevelopmentDefaults);
    }
}
