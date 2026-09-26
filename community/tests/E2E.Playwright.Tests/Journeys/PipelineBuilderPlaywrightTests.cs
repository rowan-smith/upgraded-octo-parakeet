using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace E2E.Playwright.Tests.Journeys;

/// <summary>
/// Navigation helpers for the pipeline builder. The SPA paints its navigation synchronously but
/// finishes each page from an awaited fetch, so a still-running render can replace the content after
/// the heading is already visible. Waiting for the network to go quiet keeps interactions attached.
/// </summary>
internal static class BuilderUi
{
    public static async Task OpenPipelineListAsync(IPage page)
    {
        await Ui.EnsureProjectNavAsync(page);
        await SettleAsync(page);
        await Ui.NavigateAsync(page, "/pipelines");
        await Ui.ExpectVisible(page, "h1", "Build");
        await SettleAsync(page);
    }

    public static async Task OpenNewBuilderAsync(IPage page)
    {
        await OpenPipelineListAsync(page);
        await Ui.ClickAsync(page.Locator("#newPipelineHint"));
        await WaitForBuilderAsync(page);
    }

    public static async Task OpenEditBuilderAsync(IPage page, ILocator editButton)
    {
        await OpenPipelineListAsync(page);
        await Ui.ClickAsync(editButton);
        await WaitForBuilderAsync(page);
    }

    public static async Task WaitForBuilderAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("#pipelineBuilder")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Assertions.Expect(page.Locator(".job-template-card").First).ToBeVisibleAsync(new() { Timeout = 15000 });
        await SettleAsync(page);
    }

    public static Task SettleAsync(IPage page) => page.WaitForLoadStateAsync(LoadState.NetworkIdle);
}

/// <summary>Read-only and additive builder journeys that can share one seeded host.</summary>
[Collection("playwright")]
public sealed class PipelineBuilderPlaywrightTests(PlaywrightBrowserFixture browser, SeededHostFixture seeded)
    : IClassFixture<SeededHostFixture>
{
    private ForgeDeckHost Host => seeded.Host;
    private async Task<PageSession> NewSessionAsync() => await Host.NewPageAsync(browser.Browser);

    [Fact]
    public async Task New_pipeline_button_opens_the_builder()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenPipelineListAsync(page);

        await Ui.ClickAsync(page.Locator("#newPipelineHint"));

        await BuilderUi.WaitForBuilderAsync(page);
        await Assertions.Expect(page.Locator("#content h1")).ToContainTextAsync("New pipeline");
    }

    [Fact]
    public async Task Builder_route_can_be_deep_linked()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenPipelineListAsync(page);

        await Ui.GoToHashAsync(page, "/pipelines/new");

        await BuilderUi.WaitForBuilderAsync(page);
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("New pipeline");
    }

    [Theory]
    [InlineData("#pipelineBuilder")]
    [InlineData("#pipelineName")]
    [InlineData("#pipelineTimeout")]
    [InlineData("#pipelineBuilderSave")]
    [InlineData("#addEmptyJob")]
    [InlineData(".job-catalog")]
    [InlineData(".job-template-card")]
    [InlineData("[data-builder-tab='visual']")]
    [InlineData("[data-builder-tab='yaml']")]
    [InlineData(".trigger-row")]
    public async Task Builder_exposes_control(string selector)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);

        await Assertions.Expect(page.Locator(selector).First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Validation_banner_starts_hidden()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);

        await Assertions.Expect(page.Locator("#pipelineBuilderError")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("Manual")]
    [InlineData("Push")]
    [InlineData("ChangeOpened")]
    [InlineData("ChangeUpdated")]
    [InlineData("ChangeMerged")]
    public async Task Trigger_checkbox_is_offered(string trigger)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);

        var checkbox = page.Locator($"[data-trigger='{trigger}']");
        await Assertions.Expect(checkbox).ToBeVisibleAsync();
        await checkbox.SetCheckedAsync(true);
        await Assertions.Expect(checkbox).ToBeCheckedAsync();
    }

    [Theory]
    [InlineData("dotnet-build")]
    [InlineData("dotnet-test")]
    [InlineData("node-build")]
    [InlineData("docker-build")]
    [InlineData("shell-script")]
    [InlineData("publish-artifacts")]
    public async Task Premade_job_card_is_listed(string templateId)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);

        await Assertions.Expect(page.Locator($"[data-insert-template='{templateId}']")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("dotnet-build")]
    [InlineData("dotnet-test")]
    [InlineData("node-build")]
    [InlineData("docker-build")]
    [InlineData("shell-script")]
    [InlineData("publish-artifacts")]
    public async Task Premade_job_can_be_inserted(string templateId)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);

        await Ui.ClickAsync(page.Locator($"[data-insert-template='{templateId}']"));

        await Assertions.Expect(page.Locator(".job-block")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".job-block .step-row").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Empty_job_can_be_added_and_removed()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);

        await Ui.ClickAsync(page.Locator("#addEmptyJob"));
        await Assertions.Expect(page.Locator(".job-block")).ToHaveCountAsync(1);

        await Ui.ClickAsync(page.Locator("[data-remove-job='0']"));
        await Assertions.Expect(page.Locator(".job-block")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#jobsEditor")).ToContainTextAsync("No jobs yet");
    }

    [Fact]
    public async Task Steps_can_be_added_and_removed()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);
        await Ui.ClickAsync(page.Locator("#addEmptyJob"));

        await Ui.ClickAsync(page.Locator("[data-add-step='0']"));
        await Assertions.Expect(page.Locator(".job-block .step-row")).ToHaveCountAsync(2);

        await Ui.ClickAsync(page.Locator("[data-remove-step='0:1']"));
        await Assertions.Expect(page.Locator(".job-block .step-row")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Jobs_can_be_reordered()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);
        await Ui.ClickAsync(page.Locator("[data-insert-template='dotnet-build']"));
        await Ui.ClickAsync(page.Locator("[data-insert-template='node-build']"));

        Assert.Equal("Build", await page.Locator("[data-job-name]").First.InputValueAsync());

        await Ui.ClickAsync(page.Locator("[data-move-job='1'][data-dir='up']"));

        Assert.Equal("Node Build", await page.Locator("[data-job-name]").First.InputValueAsync());
        await Assertions.Expect(page.Locator("[data-move-job='0'][data-dir='up']")).ToBeDisabledAsync();
    }

    [Theory]
    [InlineData("visual")]
    [InlineData("yaml")]
    public async Task Tab_can_be_activated(string tab)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);

        await Ui.ClickAsync(page.Locator($"[data-builder-tab='{tab}']"));

        await Assertions.Expect(page.Locator($"[data-builder-tab='{tab}']")).ToHaveClassAsync(new Regex("active"));
        if (tab == "yaml")
        {
            await Assertions.Expect(page.Locator("#pipelineYaml")).ToBeVisibleAsync();
        }
        else
        {
            await Assertions.Expect(page.Locator("#pipelineName")).ToBeVisibleAsync();
        }
    }

    [Fact]
    public async Task Yaml_tab_renders_the_visual_draft()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);
        await page.Locator("#pipelineName").FillAsync("Rendered Draft");
        await Ui.ClickAsync(page.Locator("[data-insert-template='dotnet-build']"));

        await Ui.ClickAsync(page.Locator("[data-builder-tab='yaml']"));

        var yaml = await page.Locator("#pipelineYaml").InputValueAsync();
        Assert.Contains("name: Rendered Draft", yaml, StringComparison.Ordinal);
        Assert.Contains("dotnet restore", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Yaml_edits_flow_back_into_the_visual_editor()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);
        await Ui.ClickAsync(page.Locator("[data-builder-tab='yaml']"));

        await page.Locator("#pipelineYaml").FillAsync(
            "name: From Yaml Tab\ntriggers: [manual, push]\njobs:\n  - name: Verify\n    steps:\n      - name: Test\n        command: dotnet test");
        await Ui.ClickAsync(page.Locator("[data-builder-tab='visual']"));

        Assert.Equal("From Yaml Tab", await page.Locator("#pipelineName").InputValueAsync());
        Assert.Equal("Verify", await page.Locator("[data-job-name]").First.InputValueAsync());
        await Assertions.Expect(page.Locator("[data-trigger='Push']")).ToBeCheckedAsync();
    }

    [Theory]
    [InlineData("name: [unclosed")]
    [InlineData("name: Broken")]
    [InlineData("- not\n- a\n- mapping")]
    [InlineData("name: P\ntriggers: [nightly]\njobs:\n  - name: B\n    steps:\n      - command: echo hi")]
    [InlineData("name: P\njobs:\n  - name: B\n    steps: []")]
    public async Task Invalid_yaml_keeps_the_yaml_tab_and_reports_the_error(string yaml)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);
        await Ui.ClickAsync(page.Locator("[data-builder-tab='yaml']"));

        await page.Locator("#pipelineYaml").FillAsync(yaml);
        await Ui.ClickAsync(page.Locator("[data-builder-tab='visual']"));

        await Assertions.Expect(page.Locator("#pipelineBuilderError")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#pipelineYaml")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Missing_name_blocks_a_visual_save()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);
        await Ui.ClickAsync(page.Locator("[data-insert-template='shell-script']"));
        await page.Locator("#pipelineName").FillAsync("");

        await Ui.ClickAsync(page.Locator("#pipelineBuilderSave"));

        await Assertions.Expect(page.Locator("#pipelineBuilderError")).ToContainTextAsync("name is required");
        await Assertions.Expect(page.Locator("#pipelineBuilder")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Missing_jobs_blocks_a_visual_save()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);

        await Ui.ClickAsync(page.Locator("#pipelineBuilderSave"));

        await Assertions.Expect(page.Locator("#pipelineBuilderError")).ToContainTextAsync("at least one job");
    }

    [Fact]
    public async Task Missing_job_name_blocks_a_visual_save()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);
        await Ui.ClickAsync(page.Locator("#addEmptyJob"));
        await page.Locator("[data-job-name]").First.FillAsync("");

        await Ui.ClickAsync(page.Locator("#pipelineBuilderSave"));

        await Assertions.Expect(page.Locator("#pipelineBuilderError")).ToContainTextAsync("requires a name");
    }

    [Fact]
    public async Task Missing_step_command_blocks_a_visual_save()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);
        await Ui.ClickAsync(page.Locator("#addEmptyJob"));
        await page.Locator("[data-step-command]").First.FillAsync("");

        await Ui.ClickAsync(page.Locator("#pipelineBuilderSave"));

        await Assertions.Expect(page.Locator("#pipelineBuilderError")).ToContainTextAsync("requires a command");
    }

    [Fact]
    public async Task Visual_save_creates_a_pipeline_and_returns_to_the_list()
    {
        var name = $"Visual {Guid.NewGuid():N}"[..20];
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);
        await page.Locator("#pipelineName").FillAsync(name);
        await Ui.ClickAsync(page.Locator("[data-insert-template='dotnet-build']"));

        await Ui.ClickAsync(page.Locator("#pipelineBuilderSave"));

        await Ui.ExpectToastAsync(page, "Pipeline saved");
        await Assertions.Expect(page.Locator(".pipeline-row").Filter(new() { HasText = name }).First)
            .ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    [Fact]
    public async Task Yaml_save_creates_a_pipeline()
    {
        var name = $"Yaml {Guid.NewGuid():N}"[..18];
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);
        await Ui.ClickAsync(page.Locator("[data-builder-tab='yaml']"));
        await page.Locator("#pipelineYaml").FillAsync(
            $"name: {name}\ntriggers: [manual]\njobs:\n  - name: Build\n    steps:\n      - command: dotnet build");

        await Ui.ClickAsync(page.Locator("#pipelineBuilderSave"));

        await Ui.ExpectToastAsync(page, "Pipeline saved");
        await Assertions.Expect(page.Locator(".pipeline-row").Filter(new() { HasText = name }).First)
            .ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    [Fact]
    public async Task Yaml_save_surfaces_server_validation_errors()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);
        await Ui.ClickAsync(page.Locator("[data-builder-tab='yaml']"));
        await page.Locator("#pipelineYaml").FillAsync("name: Nope");

        await Ui.ClickAsync(page.Locator("#pipelineBuilderSave"));

        await Assertions.Expect(page.Locator("#pipelineBuilderError")).ToContainTextAsync("at least one job");
        await Assertions.Expect(page.Locator("#pipelineBuilder")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Cancel_returns_to_the_pipeline_list()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);

        await Ui.ClickAsync(page.Locator("#pipelineBuilder [data-route='/pipelines']"));

        await Ui.ExpectVisible(page, "h1", "Build");
    }

    [Fact]
    public async Task Edit_button_loads_the_existing_definition()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenPipelineListAsync(page);

        await Ui.ClickAsync(page.Locator(".pipeline-row")
            .Filter(new() { HasText = ".NET Validation" })
            .Locator("[data-edit-pipeline]"));

        await BuilderUi.WaitForBuilderAsync(page);
        await Assertions.Expect(page.Locator("#content h1")).ToContainTextAsync("Edit pipeline");
        Assert.Equal(".NET Validation", await page.Locator("#pipelineName").InputValueAsync());
        Assert.True(await page.Locator(".job-block").CountAsync() > 1);
    }

    [Fact]
    public async Task Edit_yaml_tab_shows_the_server_rendered_document()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenPipelineListAsync(page);
        await Ui.ClickAsync(page.Locator(".pipeline-row")
            .Filter(new() { HasText = ".NET Validation" })
            .Locator("[data-edit-pipeline]"));
        await BuilderUi.WaitForBuilderAsync(page);

        await Ui.ClickAsync(page.Locator("[data-builder-tab='yaml']"));

        var yaml = await page.Locator("#pipelineYaml").InputValueAsync();
        Assert.Contains("name: .NET Validation", yaml, StringComparison.Ordinal);
        Assert.Contains("requiresCapabilities:", yaml, StringComparison.Ordinal);
        Assert.Contains("dotnet restore", yaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("jobs:")]
    [InlineData("steps:")]
    [InlineData("timeoutSeconds:")]
    [InlineData("triggers:")]
    [InlineData("publishCheck:")]
    public async Task Rendered_yaml_contains_schema_key(string key)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenNewBuilderAsync(page);
        await Ui.ClickAsync(page.Locator("[data-insert-template='dotnet-test']"));
        await Ui.ClickAsync(page.Locator("[data-builder-tab='yaml']"));

        var yaml = await page.Locator("#pipelineYaml").InputValueAsync();
        Assert.Contains(key, yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pipeline_templates_card_opens_the_builder()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await BuilderUi.OpenPipelineListAsync(page);

        await Ui.ClickAsync(page.Locator("[data-template]").First);

        await BuilderUi.WaitForBuilderAsync(page);
    }
}

/// <summary>Builder journeys that mutate the seeded definition, so each gets its own host.</summary>
[Collection("playwright")]
public sealed class PipelineBuilderEditPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Renaming_an_existing_pipeline_persists()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await BuilderUi.OpenPipelineListAsync(page);
        await Ui.ClickAsync(page.Locator("[data-edit-pipeline]").First);
        await BuilderUi.WaitForBuilderAsync(page);

        await page.Locator("#pipelineName").FillAsync("Renamed Validation");
        await Ui.ClickAsync(page.Locator("#pipelineBuilderSave"));

        await Ui.ExpectToastAsync(page, "Pipeline saved");
        await Assertions.Expect(page.Locator(".pipeline-row").First).ToContainTextAsync("Renamed Validation");
    }

    [Fact]
    public async Task Editing_through_the_yaml_tab_persists()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await BuilderUi.OpenPipelineListAsync(page);
        await Ui.ClickAsync(page.Locator("[data-edit-pipeline]").First);
        await BuilderUi.WaitForBuilderAsync(page);

        await Ui.ClickAsync(page.Locator("[data-builder-tab='yaml']"));
        await page.Locator("#pipelineYaml").FillAsync(
            "name: Yaml Edited\ntriggers: [manual]\njobs:\n  - name: Only\n    steps:\n      - command: echo done");
        await Ui.ClickAsync(page.Locator("#pipelineBuilderSave"));

        await Ui.ExpectToastAsync(page, "Pipeline saved");
        await Assertions.Expect(page.Locator(".pipeline-row").First).ToContainTextAsync("Yaml Edited");
        await Assertions.Expect(page.Locator(".pipeline-row").First).ToContainTextAsync("1 jobs");
    }

    [Fact]
    public async Task Adding_a_premade_job_to_an_existing_pipeline_persists()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await BuilderUi.OpenPipelineListAsync(page);
        await Ui.ClickAsync(page.Locator("[data-edit-pipeline]").First);
        await BuilderUi.WaitForBuilderAsync(page);
        var before = await page.Locator(".job-block").CountAsync();

        await Ui.ClickAsync(page.Locator("[data-insert-template='docker-build']"));
        await Assertions.Expect(page.Locator(".job-block")).ToHaveCountAsync(before + 1);
        await Ui.ClickAsync(page.Locator("#pipelineBuilderSave"));

        await Ui.ExpectToastAsync(page, "Pipeline saved");
        await Assertions.Expect(page.Locator(".pipeline-row").First).ToContainTextAsync($"{before + 1} jobs");
    }
}
