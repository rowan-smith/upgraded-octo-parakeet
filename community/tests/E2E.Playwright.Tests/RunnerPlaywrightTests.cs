using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace E2E.Playwright.Tests;

[Collection("playwright")]
public sealed class RunnerPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Runners_page_loads_with_add_and_refresh()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenRunnersAsync(page);
        await Assertions.Expect(page.Locator("#addRunner")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#refreshRunners")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(12)]
    [InlineData(24)]
    public async Task Add_runner_modal_issues_token_for_lifetime_hours(int hours)
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenRunnersAsync(page);
        await Ui.ClickAsync(page.Locator("#addRunner"));
        await Assertions.Expect(page.Locator(".modal-content h2")).ToContainTextAsync("Add runner");
        await page.Locator("#tokenHours").FillAsync(hours.ToString());
        await page.Locator("button[value='submit']").ClickAsync();
        await Assertions.Expect(page.Locator("#runnerTokenValue")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        var token = await page.Locator("#runnerTokenValue").InputValueAsync();
        Assert.False(string.IsNullOrWhiteSpace(token));
        await page.Locator("button[value='close']").ClickAsync();
    }

    [Fact]
    public async Task Registered_runner_appears_after_api_register_and_refresh()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await RegisterRunnerViaApiAsync(host, "pw-visible-runner");

        await Ui.OpenRunnersAsync(page);
        await Ui.ClickAsync(page.Locator("#refreshRunners"));
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("pw-visible-runner", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
    }

    [Theory]
    [InlineData("/pipelines", "Build")]
    [InlineData("/runs", "Runs")]
    [InlineData("/jobs", "Jobs")]
    [InlineData("/tests", "Tests")]
    [InlineData("/artifacts", "Artifacts")]
    [InlineData("/environments", "Environments")]
    [InlineData("/deployments", "Deployments")]
    [InlineData("/changes", "Pull Requests")]
    public async Task Project_module_routes_render(string route, string heading)
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.EnsureProjectNavAsync(page);
        await Ui.NavigateAsync(page, route);
        await Ui.ExpectVisible(page, "h1", heading);
    }

    [Fact]
    public async Task Overview_route_renders_project_hero()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenOverviewAsync(page);
        await Assertions.Expect(page.Locator(".project-hero h1")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Nav_keeps_runners_under_organisation_settings()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.EnsureProjectNavAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/runners']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/pipelines']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/environments']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/deployments']")).ToBeVisibleAsync();

        await Ui.OpenRunnersAsync(page);
        await Assertions.Expect(page.Locator(".settings-nav-item.active[data-route='/organisation/settings/build']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Pipeline_run_via_api_shows_on_runs_page()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        var runId = await PipelinesPlaywrightTestsHelpers.StartRunViaApiAsync(host);
        await Ui.OpenRunsAsync(page);
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync(runId.ToString("N")[..8], new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
    }

    [Theory]
    [InlineData("alpha")]
    [InlineData("beta")]
    [InlineData("gamma")]
    [InlineData("delta")]
    public async Task Multiple_runners_can_be_registered_via_api(string suffix)
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await RegisterRunnerViaApiAsync(host, $"pw-runner-{suffix}");
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await Ui.OpenRunnersAsync(page);
        await Ui.ClickAsync(page.Locator("#refreshRunners"));
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync($"pw-runner-{suffix}", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
    }

    private static async Task RegisterRunnerViaApiAsync(ForgeDeckHost host, string name)
    {
        using var login = await host.Api.PostAsJsonAsync("/api/auth/login", new { email = "maya@forgedeck.dev", password = "demo" });
        login.EnsureSuccessStatusCode();
        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var bearer = loginDoc.RootElement.GetProperty("token").GetString()!;

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "/api/pipelines/runners/registration-tokens")
        {
            Content = JsonContent.Create(new { lifetimeHours = 2 })
        };
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var tokenResponse = await host.Api.SendAsync(tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var registrationToken = tokenDoc.RootElement.GetProperty("token").GetString()!;

        using var registerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/pipelines/runners/register")
        {
            Content = JsonContent.Create(new
            {
                name,
                registrationToken,
                operatingSystem = "linux",
                capabilities = new[] { "dotnet" },
                concurrency = 1,
                version = "1.0.0"
            })
        };
        using var register = await host.Api.SendAsync(registerRequest);
        register.EnsureSuccessStatusCode();
    }
}
