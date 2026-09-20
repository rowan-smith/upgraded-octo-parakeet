using Microsoft.Playwright;

namespace Tests.E2E.Playwright;

[Collection("playwright")]
public sealed class AuthenticationPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Sign_out_returns_to_login_and_sign_in_restores_workspace()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await Ui.CompleteSetupAsync(page, email: "auth-owner@example.com", username: "authowner");

        await Ui.ClickAsync(page.Locator("#userMenu"));
        await Ui.ClickAsync(page.Locator("button[value='signout']"));
        await Ui.ExpectVisible(page, "h1", "Sign in");

        await Ui.SignInAsync(page, "auth-owner@example.com", "password123");
        await Assertions.Expect(page.Locator("#userHandle")).ToHaveTextAsync("@authowner");
        await Assertions.Expect(page.Locator("#orgLabel")).ToHaveTextAsync("Northstar Engineering");
    }

    [Fact]
    public async Task Wrong_password_shows_error_toast()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await Ui.CompleteSetupAsync(page, email: "wrong-pw@example.com", username: "wrongpw");

        await Ui.ClickAsync(page.Locator("#userMenu"));
        await Ui.ClickAsync(page.Locator("button[value='signout']"));
        await Ui.ExpectVisible(page, "h1", "Sign in");

        await page.Locator("#loginEmail").FillAsync("wrong-pw@example.com");
        await page.Locator("#loginPassword").FillAsync("not-the-password");
        await page.Locator("#loginSubmit").ClickAsync();
        await Assertions.Expect(page.Locator("#toast.error")).ToBeVisibleAsync();
    }
}

[Collection("playwright")]
public sealed class PeoplePlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task People_lists_owner_and_can_create_team()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await Ui.CompleteSetupAsync(page, email: "people-owner@example.com", username: "peopleowner");

        await Ui.OpenPeopleAsync(page);
        await Assertions.Expect(page.Locator(".module-card").First).ToContainTextAsync("peopleowner");

        await page.Locator("#peopleTeams").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Teams");
        await page.Locator("#createTeam").ClickAsync();
        await page.Locator("#teamName").FillAsync("Backend");
        await page.Locator("#teamSlug").FillAsync("backend");
        await page.Locator("button[value='submit']").ClickAsync();
        await Ui.ExpectToastAsync(page, "Team created");
        await Assertions.Expect(page.Locator(".module-card")).ToContainTextAsync("Backend");
    }

    [Fact]
    public async Task Invite_creates_copyable_link_modal()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await Ui.CompleteSetupAsync(page, email: "invite-ui@example.com", username: "inviteui");

        await Ui.OpenPeopleAsync(page);
        await page.Locator("#inviteMember").ClickAsync();
        await page.Locator("#inviteEmail").FillAsync("alice-ui@example.com");
        await page.Locator("#inviteRole").SelectOptionAsync("Member");
        await page.Locator("button[value='submit']").ClickAsync();

        await Assertions.Expect(page.Locator("#inviteLink")).ToBeVisibleAsync();
        var link = await page.Locator("#inviteLink").InputValueAsync();
        Assert.Contains("/#/invite/", link);
    }

    [Fact]
    public async Task Invitations_tab_lists_pending_invites()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await Ui.CompleteSetupAsync(page, email: "invites-tab@example.com", username: "invitestab");

        await Ui.OpenPeopleAsync(page);
        await page.Locator("#inviteMember").ClickAsync();
        await page.Locator("#inviteEmail").FillAsync("pending@example.com");
        await page.Locator("button[value='submit']").ClickAsync();
        await page.Locator("button[value='copy']").ClickAsync();

        await page.Locator("#peopleInvites").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Invitations");
        await Assertions.Expect(page.Locator(".module-card")).ToContainTextAsync("pending@example.com");
    }
}

[Collection("playwright")]
public sealed class ProjectSwitcherPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Project_switcher_can_create_and_switch_projects()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await Ui.CompleteSetupAsync(page, email: "switcher@example.com", username: "switcher");

        await page.Locator("#contextButton").ClickAsync();
        await Assertions.Expect(page.Locator(".modal-content h2")).ToHaveTextAsync("Projects");
        await page.Locator("button[value='new']").ClickAsync();
        await page.Locator("#newProjectName").FillAsync("Stockyards");
        await page.Locator("#newProjectSlug").FillAsync("stockyards");
        await page.Locator("button[value='submit']").ClickAsync();
        await Ui.ExpectToastAsync(page, "Project created");
        await Assertions.Expect(page.Locator("#projectLabel")).ToHaveTextAsync("Stockyards", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
    }
}
