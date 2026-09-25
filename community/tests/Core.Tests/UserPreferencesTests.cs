using ForgeDeck.Core.Application;
using ForgeDeck.Core.Domain;

namespace Core.Tests;

public sealed class UserPreferencesTests
{
    [Fact]
    public void Profile_theme_defaults_to_system_and_persists()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var profile = fixture.Store.GetProfile(owner.Id)!;
        Assert.Equal("system", profile.Theme);

        profile.Theme = "dark";
        fixture.Store.SaveProfile(profile);

        var reloaded = fixture.Store.GetProfile(owner.Id)!;
        Assert.Equal("dark", reloaded.Theme);
    }
}
