using Microsoft.Playwright;

namespace E2E.Playwright.Tests;

[Collection("playwright")]
public sealed class DeployPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Environments_page_loads_and_shows_empty_state()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenEnvironmentsAsync(page);
        await Assertions.Expect(page.Locator("#createEnvironment")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".empty.small")).ToContainTextAsync("No environments yet");
    }

    [Fact]
    public async Task Create_environment_from_ui()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenEnvironmentsAsync(page);
        await Ui.ClickAsync(page.Locator("#createEnvironment"));
        await Ui.ExpectModalOpenAsync(page, "New environment");
        await page.Locator("#modal[open] #envName").FillAsync("production");
        await page.Locator("#modal[open] #envDescription").FillAsync("Primary");
        await page.Locator("#modal[open] button[value='submit']").ClickAsync();
        await Ui.ExpectToastAsync(page, "Environment created");
        await Assertions.Expect(page.Locator("table.table")).ToContainTextAsync("production");
    }

    [Fact]
    public async Task Create_deployment_and_rollback_from_ui()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenEnvironmentsAsync(page);
        await Ui.ClickAsync(page.Locator("#createEnvironment"));
        await page.Locator("#modal[open] #envName").FillAsync("production");
        await page.Locator("#modal[open] button[value='submit']").ClickAsync();
        await Ui.ExpectToastAsync(page, "Environment created");

        await Ui.OpenDeploymentsAsync(page);
        await Ui.ClickAsync(page.Locator("#createDeployment"));
        await Ui.ExpectModalOpenAsync(page, "New deployment");
        await page.Locator("#modal[open] #depVersion").FillAsync("1.0.0");
        await page.Locator("#modal[open] button[value='submit']").ClickAsync();
        await Ui.ExpectToastAsync(page, "Deployment created");
        await Assertions.Expect(page.Locator("table.table")).ToContainTextAsync("1.0.0");

        await Ui.ClickAsync(page.Locator("[data-rollback]").First);
        await Ui.ExpectToastAsync(page, "Rollback created");
        await Assertions.Expect(page.Locator("table.table")).ToContainTextAsync("rollback");
    }

    [Fact]
    public async Task Deployments_nav_visible_when_module_enabled()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.EnsureProjectNavAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/environments']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/deployments']")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("/environments", "Environments")]
    [InlineData("/deployments", "Deployments")]
    public async Task Deploy_hash_routes_render_headings(string route, string heading)
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.EnsureProjectNavAsync(page);
        await Ui.NavigateAsync(page, route);
        await Ui.ExpectVisible(page, "h1", heading);
    }
}
