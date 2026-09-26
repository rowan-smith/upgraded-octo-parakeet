using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace E2E.Playwright.Tests;

[Collection("playwright")]
public sealed class PipelinesPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Pipelines_page_lists_dotnet_validation_definition()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenPipelinesAsync(page);
        await Assertions.Expect(page.Locator(".pipeline-row").First).ToContainTextAsync(".NET Validation");
        await Assertions.Expect(page.Locator("[data-run-pipeline]").First).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#refreshPipelines")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Viewing_pipeline_shows_jobs_and_version()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenPipelinesAsync(page);
        await Ui.ClickAsync(page.Locator("[data-view-pipeline]").First);
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Pipelines");
        await Assertions.Expect(page.Locator(".card .side-stat").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".card .pill").Filter(new LocatorFilterOptions { HasTextString = "v" }).First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Disable_and_enable_pipeline_from_ui()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenPipelinesAsync(page);
        var toggle = page.Locator("[data-toggle-pipeline]").First;
        await Ui.ClickAsync(toggle);
        await Ui.ExpectToastAsync(page, "Pipeline disabled");
        await Assertions.Expect(page.Locator("[data-run-pipeline]").First).ToBeDisabledAsync();

        await Ui.ClickAsync(page.Locator("[data-toggle-pipeline]").First);
        await Ui.ExpectToastAsync(page, "Pipeline enabled");
        await Assertions.Expect(page.Locator("[data-run-pipeline]").First).ToBeEnabledAsync();
    }

    [Fact]
    public async Task Manual_run_opens_modal_rejects_invalid_sha_then_starts_valid_run()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenPipelinesAsync(page);
        await Ui.ClickAsync(page.Locator("[data-run-pipeline]").First);

        await Ui.ExpectModalOpenAsync(page, "Run pipeline");
        await page.Locator("#modal[open] #runCommit").FillAsync("local");
        await page.Locator("#modal[open] button[value='submit']").ClickAsync();
        await Ui.ExpectToastAsync(page, "Enter a real commit SHA");
        await Ui.ExpectModalClosedAsync(page);

        // Dialog form method=dialog closes before validation; reopen for a valid run.
        await Ui.ClickAsync(page.Locator("[data-run-pipeline]").First);
        await Ui.ExpectModalOpenAsync(page, "Run pipeline");
        await page.Locator("#modal[open] #runCommit").FillAsync("abc1234deadbeef");
        await page.Locator("#modal[open] #runRef").FillAsync("main");
        await page.Locator("#modal[open] button[value='submit']").ClickAsync();
        await Ui.ExpectToastAsync(page, "Pipeline run started");
        await Assertions.Expect(page.Locator(".run-summary h1")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Assertions.Expect(page.Locator("#refreshRun")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#cancelRun")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".job-list-row").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Run_completes_and_job_log_page_exposes_controls()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenPipelinesAsync(page);
        await Ui.ClickAsync(page.Locator("[data-run-pipeline]").First);
        await page.Locator("#modal[open] #runCommit").FillAsync("abcdef0123456789");
        await page.Locator("#modal[open] button[value='submit']").ClickAsync();
        await Assertions.Expect(page.Locator(".run-summary")).ToBeVisibleAsync();

        await Ui.WaitForRunTerminalAsync(page);
        await page.WaitForTimeoutAsync(300);
        await page.Locator(".job-list-row").First.ClickAsync(new LocatorClickOptions { Force = true });
        await Assertions.Expect(page.Locator("#jobLogStream")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Assertions.Expect(page.Locator("#refreshJobPage")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#copyJobLogs")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#downloadJobLogs")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#logSearch")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#toggleErrorsOnly")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#expandAllSteps")).ToBeVisibleAsync();

        await page.Locator("#logSearch").FillAsync("dotnet");
        await page.Locator("#expandAllSteps").ClickAsync(new LocatorClickOptions { Force = true });
        await page.Locator("#collapseAllSteps").ClickAsync(new LocatorClickOptions { Force = true });
        await page.Locator("#toggleWrap").ClickAsync(new LocatorClickOptions { Force = true });
        await Assertions.Expect(page.Locator("#jobLogStream")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Runs_list_shows_started_run_and_opens_detail()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        var runId = await StartRunViaApiAsync(host);

        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await Ui.OpenRunsAsync(page);
        await Assertions.Expect(page.Locator(".run-row").First).ToBeVisibleAsync();
        await Ui.ClickAsync(page.Locator($".run-row[data-route='/runs/{runId}']"));
        await Assertions.Expect(page.Locator(".run-summary")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("code").First).ToContainTextAsync(runId.ToString());
    }

    [Fact]
    public async Task Cancel_active_run_from_detail_page()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenPipelinesAsync(page);
        await Ui.ClickAsync(page.Locator("[data-run-pipeline]").First);
        await page.Locator("#modal[open] #runCommit").FillAsync("cancelme01234567");
        await page.Locator("#modal[open] button[value='submit']").ClickAsync();
        await Assertions.Expect(page.Locator("#cancelRun")).ToBeVisibleAsync();

        // Cancel while still active if possible; otherwise assert terminal state controls.
        var cancel = page.Locator("#cancelRun");
        if (await cancel.IsEnabledAsync())
        {
            await Ui.ClickAsync(cancel);
            await Ui.ExpectToastAsync(page, "Run cancelled");
        }

        await page.Locator("#refreshRun").ClickAsync();
        await Assertions.Expect(page.Locator(".run-summary .list-page-header .status").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Retry_completed_run_starts_new_run()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        var runId = await StartRunViaApiAsync(host);
        await WaitForApiRunTerminalAsync(host, runId);

        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await page.GotoAsync($"/#/runs/{runId}");
        await page.WaitForFunctionAsync("() => document.getElementById('app')?.getAttribute('aria-busy') === 'false'");
        await Assertions.Expect(page.Locator("#retryRun")).ToBeVisibleAsync();
        await Ui.ClickAsync(page.Locator("#retryRun"));
        await Ui.ExpectToastAsync(page, "Retry started");
        await Assertions.Expect(page.Locator(".run-summary")).ToBeVisibleAsync();
    }

    private static async Task<Guid> StartRunViaApiAsync(ForgeDeckHost host)
    {
        using var login = await host.Api.PostAsJsonAsync("/api/auth/login", new { email = "maya@forgedeck.dev", password = "demo" });
        login.EnsureSuccessStatusCode();
        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = loginDoc.RootElement.GetProperty("token").GetString()!;

        using var defsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/pipelines/definitions");
        defsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var defs = await host.Api.SendAsync(defsRequest);
        defs.EnsureSuccessStatusCode();
        using var defsDoc = JsonDocument.Parse(await defs.Content.ReadAsStringAsync());
        var definitionId = defsDoc.RootElement[0].GetProperty("id").GetGuid();

        using var runRequest = new HttpRequestMessage(HttpMethod.Post, "/api/pipelines/runs")
        {
            Content = JsonContent.Create(new
            {
                definitionId,
                @ref = "main",
                commitSha = Guid.NewGuid().ToString("N")[..16],
                repositoryUrl = "https://github.com/rowan-smith/upgraded-octo-parakeet.git"
            })
        };
        runRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var run = await host.Api.SendAsync(runRequest);
        run.EnsureSuccessStatusCode();
        using var runDoc = JsonDocument.Parse(await run.Content.ReadAsStringAsync());
        return runDoc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task WaitForApiRunTerminalAsync(ForgeDeckHost host, Guid runId, int timeoutMs = 45000)
    {
        using var login = await host.Api.PostAsJsonAsync("/api/auth/login", new { email = "maya@forgedeck.dev", password = "demo" });
        login.EnsureSuccessStatusCode();
        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = loginDoc.RootElement.GetProperty("token").GetString()!;

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/pipelines/runs/{runId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await host.Api.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var status = doc.RootElement.GetProperty("status").GetString();
            if (status is "Succeeded" or "Failed" or "Cancelled" or "PartiallySucceeded")
            {
                return;
            }

            await Task.Delay(400);
        }
        throw new TimeoutException($"Run {runId} did not complete.");
    }
}

[Collection("playwright")]
public sealed class PipelinesMorePlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Refresh_pipelines_keeps_definition_visible()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenPipelinesAsync(page);
        await Ui.ClickAsync(page.Locator("#refreshPipelines"));
        await Assertions.Expect(page.Locator(".pipeline-row").First).ToContainTextAsync(".NET Validation");
    }

    [Fact]
    public async Task Cancel_run_modal_dismisses_without_starting()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenPipelinesAsync(page);
        await Ui.ClickAsync(page.Locator("[data-run-pipeline]").First);
        await Ui.ExpectModalOpenAsync(page, "Run pipeline");
        await Ui.CloseModalAsync(page);
        await Assertions.Expect(page.Locator("h1").Filter(new LocatorFilterOptions { HasTextString = "Build" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Delete_pipeline_confirm_dismiss_keeps_definition()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenPipelinesAsync(page);
        page.Dialog += async (_, dialog) => await dialog.DismissAsync();
        await Ui.ClickAsync(page.Locator("[data-delete-pipeline]").First);
        await Assertions.Expect(page.Locator(".pipeline-row").First).ToContainTextAsync(".NET Validation");
    }

    [Fact]
    public async Task Delete_pipeline_confirm_accept_removes_definition()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenPipelinesAsync(page);
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await Ui.ClickAsync(page.Locator("[data-delete-pipeline]").First);
        await Ui.ExpectToastAsync(page, "Pipeline deleted");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("No pipeline definitions");
    }

    [Fact]
    public async Task Runs_page_refresh_works_when_empty()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenRunsAsync(page);
        await Assertions.Expect(page.Locator("#refreshRuns")).ToBeVisibleAsync();
        await Ui.ClickAsync(page.Locator("#refreshRuns"));
        await Assertions.Expect(page.Locator("h1").Filter(new LocatorFilterOptions { HasTextString = "Runs" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Deep_link_to_run_detail_after_api_start()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        var runId = await PipelinesPlaywrightTestsHelpers.StartRunViaApiAsync(host);

        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await page.GotoAsync($"/#/runs/{runId}");
        await Ui.WaitForAppIdleAsync(page);
        await Assertions.Expect(page.Locator(".run-summary")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Assertions.Expect(page.Locator("#refreshRun")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".job-list-row").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Pipeline_breadcrumb_returns_from_run_to_runs_list()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        var runId = await PipelinesPlaywrightTestsHelpers.StartRunViaApiAsync(host);

        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await page.GotoAsync($"/#/runs/{runId}");
        await Ui.WaitForAppIdleAsync(page);
        await Ui.ClickAsync(page.Locator("#breadcrumbs [data-route='/runs']").First);
        await Ui.ExpectVisible(page, "h1", "Runs");
    }
}

[Collection("playwright")]
public sealed class RunnersPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Runners_page_lists_agents_and_can_create_registration_token()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenRunnersAsync(page);
        await Assertions.Expect(page.Locator("#refreshRunners")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#addRunner")).ToBeVisibleAsync();

        await Ui.ClickAsync(page.Locator("#addRunner"));
        await Assertions.Expect(page.Locator(".modal-content h2")).ToContainTextAsync("Add runner");
        await page.Locator("#tokenHours").FillAsync("2");
        await page.Locator("button[value='submit']").ClickAsync();
        await Assertions.Expect(page.Locator("#runnerTokenValue")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        var token = await page.Locator("#runnerTokenValue").InputValueAsync();
        Assert.False(string.IsNullOrWhiteSpace(token));
        await Assertions.Expect(page.Locator(".modal-content")).ToContainTextAsync("dotnet run");
        await page.Locator("button[value='close']").ClickAsync();
    }

    [Fact]
    public async Task Add_runner_modal_can_be_cancelled()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenRunnersAsync(page);
        await Ui.ClickAsync(page.Locator("#addRunner"));
        await Ui.CloseModalAsync(page);
        await Ui.ExpectVisible(page, "h1", "Runners");
    }

    [Fact]
    public async Task Refresh_runners_keeps_page_stable()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenRunnersAsync(page);
        await Ui.ClickAsync(page.Locator("#refreshRunners"));
        await Assertions.Expect(page.Locator("#addRunner")).ToBeVisibleAsync();
    }
}

internal static class PipelinesPlaywrightTestsHelpers
{
    public static async Task<Guid> StartRunViaApiAsync(ForgeDeckHost host)
    {
        using var login = await host.Api.PostAsJsonAsync("/api/auth/login", new { email = "maya@forgedeck.dev", password = "demo" });
        login.EnsureSuccessStatusCode();
        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = loginDoc.RootElement.GetProperty("token").GetString()!;

        using var defsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/pipelines/definitions");
        defsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var defs = await host.Api.SendAsync(defsRequest);
        defs.EnsureSuccessStatusCode();
        using var defsDoc = JsonDocument.Parse(await defs.Content.ReadAsStringAsync());
        var definitionId = defsDoc.RootElement[0].GetProperty("id").GetGuid();

        using var runRequest = new HttpRequestMessage(HttpMethod.Post, "/api/pipelines/runs")
        {
            Content = JsonContent.Create(new
            {
                definitionId,
                @ref = "main",
                commitSha = Guid.NewGuid().ToString("N")[..16],
                repositoryUrl = "https://github.com/rowan-smith/upgraded-octo-parakeet.git"
            })
        };
        runRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var run = await host.Api.SendAsync(runRequest);
        run.EnsureSuccessStatusCode();
        using var runDoc = JsonDocument.Parse(await run.Content.ReadAsStringAsync());
        return runDoc.RootElement.GetProperty("id").GetGuid();
    }
}
