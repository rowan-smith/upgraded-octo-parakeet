using ForgeDeck.Build.Domain;
using ForgeDeck.Build.Serialization;

namespace Core.Tests;

public sealed class PipelineYamlTests
{
    private const string SampleYaml = """
        name: My Pipeline
        enabled: true
        timeoutSeconds: 3600
        triggers: [manual, push, changeOpened]
        environment:
          KEY: value
        jobs:
          - name: Build
            requiresCapabilities: [dotnet]
            timeoutSeconds: 900
            publishCheck: true
            checkName: build
            artifactGlobs: ["**/bin/**"]
            continueOnError: false
            environment: {}
            steps:
              - name: Restore
                command: dotnet restore
                shell: bash
                timeoutSeconds: 300
                continueOnError: false
        """;

    [Fact]
    public void Sample_yaml_maps_every_documented_field()
    {
        var definition = PipelineYamlMapper.FromYaml(SampleYaml);

        Assert.Equal("My Pipeline", definition.Name);
        Assert.True(definition.Enabled);
        Assert.Equal(3600, definition.TimeoutSeconds);
        Assert.Equal([PipelineTrigger.Manual, PipelineTrigger.Push, PipelineTrigger.ChangeOpened], definition.Triggers);
        Assert.Equal("value", definition.Environment["KEY"]);

        var job = Assert.Single(definition.Jobs);
        Assert.Equal("Build", job.Name);
        Assert.Equal(["dotnet"], job.RequiresCapabilities);
        Assert.Equal(900, job.TimeoutSeconds);
        Assert.True(job.PublishCheck);
        Assert.Equal("build", job.CheckName);
        Assert.Equal(["**/bin/**"], job.ArtifactGlobs);
        Assert.False(job.ContinueOnError);
        Assert.Empty(job.Environment);

        var step = Assert.Single(job.Steps);
        Assert.Equal("Restore", step.Name);
        Assert.Equal("dotnet restore", step.Command);
        Assert.Equal("bash", step.Shell);
        Assert.Equal(300, step.TimeoutSeconds);
        Assert.False(step.ContinueOnError);
    }

    [Fact]
    public void Round_trip_preserves_definition()
    {
        var original = PipelineYamlMapper.FromYaml(SampleYaml);
        var restored = PipelineYamlMapper.FromYaml(PipelineYamlMapper.ToYaml(original));

        AssertEquivalent(original, restored);
    }

    [Fact]
    public void Round_trip_is_stable_after_two_passes()
    {
        var first = PipelineYamlMapper.Normalise(SampleYaml);
        var second = PipelineYamlMapper.Normalise(first);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Round_trip_preserves_multi_job_definition()
    {
        var original = new PipelineDefinition
        {
            Name = "Full",
            Enabled = false,
            TimeoutSeconds = 4200,
            Triggers = [PipelineTrigger.Push, PipelineTrigger.ChangeMerged],
            Environment = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" },
            Jobs =
            [
                new PipelineJobDefinition(
                    "Build",
                    [new PipelineStepDefinition("Restore", "dotnet restore", null, new Dictionary<string, string>(), 120)],
                    ["dotnet"],
                    new Dictionary<string, string> { ["JOB"] = "yes" },
                    600,
                    PublishCheck: true,
                    CheckName: "build",
                    ArtifactGlobs: ["**/bin/**"],
                    ContinueOnError: true),
                new PipelineJobDefinition(
                    "Test",
                    [
                        new PipelineStepDefinition("Unit", "dotnet test", "pwsh", new Dictionary<string, string> { ["S"] = "1" }, 900, true),
                        new PipelineStepDefinition("Smoke", "dotnet run --project smoke", null, new Dictionary<string, string>(), 300)
                    ],
                    [],
                    new Dictionary<string, string>(),
                    1500,
                    PublishCheck: false,
                    CheckName: null,
                    ArtifactGlobs: [])
            ]
        };

        var restored = PipelineYamlMapper.FromYaml(PipelineYamlMapper.ToYaml(original));

        AssertEquivalent(original, restored);
    }

    [Fact]
    public void ToYaml_emits_camel_case_keys()
    {
        var yaml = PipelineYamlMapper.ToYaml(PipelineYamlMapper.FromYaml(SampleYaml));

        Assert.Contains("name: My Pipeline", yaml, StringComparison.Ordinal);
        Assert.Contains("timeoutSeconds:", yaml, StringComparison.Ordinal);
        Assert.Contains("publishCheck:", yaml, StringComparison.Ordinal);
        Assert.Contains("requiresCapabilities:", yaml, StringComparison.Ordinal);
        Assert.Contains("artifactGlobs:", yaml, StringComparison.Ordinal);
        Assert.Contains("continueOnError:", yaml, StringComparison.Ordinal);
        Assert.Contains("steps:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToYaml_omits_null_shell_and_check_name()
    {
        var definition = new PipelineDefinition
        {
            Name = "Minimal",
            Triggers = [PipelineTrigger.Manual],
            Jobs =
            [
                new PipelineJobDefinition(
                    "Job",
                    [new PipelineStepDefinition("Step", "echo hi", null, new Dictionary<string, string>(), 60)],
                    [],
                    new Dictionary<string, string>(),
                    120,
                    PublishCheck: false,
                    CheckName: null,
                    ArtifactGlobs: [])
            ]
        };

        var yaml = PipelineYamlMapper.ToYaml(definition);

        Assert.DoesNotContain("shell:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("checkName:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToYaml_rejects_null_definition() =>
        Assert.Throws<ArgumentNullException>(() => PipelineYamlMapper.ToYaml(null!));

    [Theory]
    [InlineData("manual", PipelineTrigger.Manual)]
    [InlineData("Manual", PipelineTrigger.Manual)]
    [InlineData("MANUAL", PipelineTrigger.Manual)]
    [InlineData("  manual  ", PipelineTrigger.Manual)]
    [InlineData("push", PipelineTrigger.Push)]
    [InlineData("Push", PipelineTrigger.Push)]
    [InlineData("PUSH", PipelineTrigger.Push)]
    [InlineData("changeOpened", PipelineTrigger.ChangeOpened)]
    [InlineData("changeopened", PipelineTrigger.ChangeOpened)]
    [InlineData("ChangeOpened", PipelineTrigger.ChangeOpened)]
    [InlineData("CHANGEOPENED", PipelineTrigger.ChangeOpened)]
    [InlineData("change-opened", PipelineTrigger.ChangeOpened)]
    [InlineData("change_opened", PipelineTrigger.ChangeOpened)]
    [InlineData("changeUpdated", PipelineTrigger.ChangeUpdated)]
    [InlineData("change-updated", PipelineTrigger.ChangeUpdated)]
    [InlineData("CHANGE_UPDATED", PipelineTrigger.ChangeUpdated)]
    [InlineData("changeMerged", PipelineTrigger.ChangeMerged)]
    [InlineData("change-merged", PipelineTrigger.ChangeMerged)]
    [InlineData("ChangeMERGED", PipelineTrigger.ChangeMerged)]
    public void Trigger_names_parse_case_insensitively(string value, PipelineTrigger expected)
    {
        Assert.True(PipelineYamlMapper.TryParseTrigger(value, out var trigger));
        Assert.Equal(expected, trigger);
    }

    [Theory]
    [InlineData("manual", PipelineTrigger.Manual)]
    [InlineData("PUSH", PipelineTrigger.Push)]
    [InlineData("change-opened", PipelineTrigger.ChangeOpened)]
    [InlineData("changeUPDATED", PipelineTrigger.ChangeUpdated)]
    [InlineData("change_merged", PipelineTrigger.ChangeMerged)]
    public void Trigger_names_parse_inside_documents(string value, PipelineTrigger expected)
    {
        var definition = PipelineYamlMapper.FromYaml(MinimalYaml($"triggers: [{value}]"));

        Assert.Equal([expected], definition.Triggers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("nope")]
    [InlineData("manuals")]
    [InlineData("change")]
    [InlineData("opened")]
    [InlineData("0")]
    public void Unknown_trigger_names_are_rejected(string? value) =>
        Assert.False(PipelineYamlMapper.TryParseTrigger(value, out _));

    [Theory]
    [InlineData("nightly")]
    [InlineData("cron")]
    [InlineData("tag")]
    [InlineData("manualy")]
    [InlineData("changeClosed")]
    public void Unknown_trigger_in_document_throws(string value)
    {
        var error = Assert.Throws<ArgumentException>(() => PipelineYamlMapper.FromYaml(MinimalYaml($"triggers: [{value}]")));

        Assert.Contains(value, error.Message, StringComparison.Ordinal);
        Assert.Contains("Valid triggers", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PipelineTrigger.Manual, "manual")]
    [InlineData(PipelineTrigger.Push, "push")]
    [InlineData(PipelineTrigger.ChangeOpened, "changeOpened")]
    [InlineData(PipelineTrigger.ChangeUpdated, "changeUpdated")]
    [InlineData(PipelineTrigger.ChangeMerged, "changeMerged")]
    public void Trigger_names_are_emitted_in_camel_case(PipelineTrigger trigger, string expected) =>
        Assert.Equal(expected, PipelineYamlMapper.TriggerName(trigger));

    [Fact]
    public void Trigger_names_expose_every_enum_member() =>
        Assert.Equal(Enum.GetValues<PipelineTrigger>().Length, PipelineYamlMapper.TriggerNames.Count);

    [Fact]
    public void Missing_triggers_default_to_manual()
    {
        var definition = PipelineYamlMapper.FromYaml(MinimalYaml(null));

        Assert.Equal([PipelineTrigger.Manual], definition.Triggers);
    }

    [Fact]
    public void Empty_trigger_list_defaults_to_manual()
    {
        var definition = PipelineYamlMapper.FromYaml(MinimalYaml("triggers: []"));

        Assert.Equal([PipelineTrigger.Manual], definition.Triggers);
    }

    [Fact]
    public void Duplicate_triggers_are_collapsed()
    {
        var definition = PipelineYamlMapper.FromYaml(MinimalYaml("triggers: [manual, Manual, push, PUSH]"));

        Assert.Equal([PipelineTrigger.Manual, PipelineTrigger.Push], definition.Triggers);
    }

    [Fact]
    public void Block_and_flow_trigger_sequences_are_equivalent()
    {
        var flow = PipelineYamlMapper.FromYaml(MinimalYaml("triggers: [manual, push]"));
        var block = PipelineYamlMapper.FromYaml(MinimalYaml("triggers:\n  - manual\n  - push"));

        Assert.Equal(flow.Triggers, block.Triggers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    [InlineData("\t")]
    public void Blank_yaml_is_rejected(string? yaml)
    {
        var error = Assert.Throws<ArgumentException>(() => PipelineYamlMapper.FromYaml(yaml));

        Assert.Contains("empty", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("name: [unclosed")]
    [InlineData("\tname: tabbed")]
    [InlineData("name: one\n  bad: indent")]
    [InlineData("- just\n- a\n- list")]
    [InlineData("plain scalar document")]
    [InlineData("name: ok\njobs: 42")]
    [InlineData("name: ok\njobs:\n  - name: Build\n    steps: nope")]
    [InlineData("name: ok\ntimeoutSeconds: not-a-number")]
    public void Invalid_yaml_is_rejected(string yaml)
    {
        var error = Assert.Throws<ArgumentException>(() => PipelineYamlMapper.FromYaml(yaml));

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Theory]
    [InlineData("enabled: true")]
    [InlineData("name: ''")]
    [InlineData("name: '   '")]
    [InlineData("timeoutSeconds: 60")]
    public void Missing_name_is_rejected(string yaml)
    {
        var error = Assert.Throws<ArgumentException>(() => PipelineYamlMapper.FromYaml($"{yaml}\njobs:\n  - name: Build\n    steps:\n      - command: echo hi"));

        Assert.Contains("name is required", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("name: No Jobs")]
    [InlineData("name: No Jobs\njobs: []")]
    [InlineData("name: No Jobs\njobs:")]
    public void Missing_jobs_is_rejected(string yaml)
    {
        var error = Assert.Throws<ArgumentException>(() => PipelineYamlMapper.FromYaml(yaml));

        Assert.Contains("at least one job", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("- name: Build")]
    [InlineData("- name: Build\n    steps: []")]
    [InlineData("- name: Build\n    steps:")]
    public void Job_without_steps_is_rejected(string jobs)
    {
        var error = Assert.Throws<ArgumentException>(() => PipelineYamlMapper.FromYaml($"name: P\njobs:\n  {jobs}"));

        Assert.Contains("at least one step", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("- steps:\n      - command: echo hi")]
    [InlineData("- name: ''\n    steps:\n      - command: echo hi")]
    [InlineData("- name: '  '\n    steps:\n      - command: echo hi")]
    public void Job_without_name_is_rejected(string jobs)
    {
        var error = Assert.Throws<ArgumentException>(() => PipelineYamlMapper.FromYaml($"name: P\njobs:\n  {jobs}"));

        Assert.Contains("requires a name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("- name: Restore")]
    [InlineData("- name: Restore\n        command: ''")]
    [InlineData("- name: Restore\n        command: '   '")]
    [InlineData("- timeoutSeconds: 30")]
    public void Step_without_command_is_rejected(string steps)
    {
        var error = Assert.Throws<ArgumentException>(() => PipelineYamlMapper.FromYaml($"name: P\njobs:\n  - name: Build\n    steps:\n      {steps}"));

        Assert.Contains("requires a command", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Build", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Step_name_defaults_to_command()
    {
        var definition = PipelineYamlMapper.FromYaml("name: P\njobs:\n  - name: Build\n    steps:\n      - command: dotnet build");

        Assert.Equal("dotnet build", definition.Jobs[0].Steps[0].Name);
    }

    [Fact]
    public void Timeouts_fall_back_to_documented_defaults()
    {
        var definition = PipelineYamlMapper.FromYaml("name: P\njobs:\n  - name: Build\n    steps:\n      - command: echo hi");

        Assert.Equal(PipelineYamlMapper.DefaultPipelineTimeoutSeconds, definition.TimeoutSeconds);
        Assert.Equal(PipelineYamlMapper.DefaultJobTimeoutSeconds, definition.Jobs[0].TimeoutSeconds);
        Assert.Equal(PipelineYamlMapper.DefaultStepTimeoutSeconds, definition.Jobs[0].Steps[0].TimeoutSeconds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(3600)]
    [InlineData(86400)]
    public void Explicit_pipeline_timeout_is_honoured(int seconds)
    {
        var definition = PipelineYamlMapper.FromYaml($"name: P\ntimeoutSeconds: {seconds}\njobs:\n  - name: Build\n    steps:\n      - command: echo hi");

        Assert.Equal(seconds, definition.TimeoutSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-3600)]
    public void Non_positive_pipeline_timeout_is_rejected(int seconds)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            PipelineYamlMapper.FromYaml($"name: P\ntimeoutSeconds: {seconds}\njobs:\n  - name: Build\n    steps:\n      - command: echo hi"));

        Assert.Contains("Pipeline timeoutSeconds", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_job_timeout_is_rejected(int seconds)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            PipelineYamlMapper.FromYaml($"name: P\njobs:\n  - name: Build\n    timeoutSeconds: {seconds}\n    steps:\n      - command: echo hi"));

        Assert.Contains("Job 'Build' timeoutSeconds", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-9)]
    public void Non_positive_step_timeout_is_rejected(int seconds)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            PipelineYamlMapper.FromYaml($"name: P\njobs:\n  - name: Build\n    steps:\n      - command: echo hi\n        timeoutSeconds: {seconds}"));

        Assert.Contains("timeoutSeconds", error.Message, StringComparison.Ordinal);
        Assert.Contains("Build", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData("False", false)]
    public void Enabled_flag_round_trips(string value, bool expected)
    {
        var definition = PipelineYamlMapper.FromYaml(MinimalYaml($"enabled: {value}"));

        Assert.Equal(expected, definition.Enabled);
        Assert.Equal(expected, PipelineYamlMapper.FromYaml(PipelineYamlMapper.ToYaml(definition)).Enabled);
    }

    [Fact]
    public void Enabled_defaults_to_true()
    {
        var definition = PipelineYamlMapper.FromYaml(MinimalYaml(null));

        Assert.True(definition.Enabled);
    }

    [Fact]
    public void Environment_maps_round_trip_at_every_level()
    {
        const string yaml = """
            name: Env
            environment:
              PIPE: one
              OTHER: two
            jobs:
              - name: Build
                environment:
                  JOB: three
                steps:
                  - command: echo hi
                    environment:
                      STEP: four
            """;

        var definition = PipelineYamlMapper.FromYaml(yaml);
        Assert.Equal("one", definition.Environment["PIPE"]);
        Assert.Equal("two", definition.Environment["OTHER"]);
        Assert.Equal("three", definition.Jobs[0].Environment["JOB"]);
        Assert.Equal("four", definition.Jobs[0].Steps[0].Environment["STEP"]);

        var restored = PipelineYamlMapper.FromYaml(PipelineYamlMapper.ToYaml(definition));
        Assert.Equal("one", restored.Environment["PIPE"]);
        Assert.Equal("three", restored.Jobs[0].Environment["JOB"]);
        Assert.Equal("four", restored.Jobs[0].Steps[0].Environment["STEP"]);
    }

    [Theory]
    [InlineData("environment: {}")]
    [InlineData("environment:")]
    public void Empty_environment_maps_to_empty_dictionary(string environment)
    {
        var definition = PipelineYamlMapper.FromYaml(MinimalYaml(environment));

        Assert.Empty(definition.Environment);
    }

    [Fact]
    public void Blank_environment_key_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() => PipelineYamlMapper.FromYaml(MinimalYaml("environment:\n  '': value")));

        Assert.Contains("cannot be blank", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Blank_capabilities_and_globs_are_dropped()
    {
        const string yaml = """
            name: Trimmed
            jobs:
              - name: Build
                requiresCapabilities: [" dotnet ", "", "  "]
                artifactGlobs: [" artifacts/** ", ""]
                steps:
                  - command: echo hi
            """;

        var definition = PipelineYamlMapper.FromYaml(yaml);

        Assert.Equal(["dotnet"], definition.Jobs[0].RequiresCapabilities);
        Assert.Equal(["artifacts/**"], definition.Jobs[0].ArtifactGlobs);
    }

    [Fact]
    public void Unknown_properties_are_ignored()
    {
        var definition = PipelineYamlMapper.FromYaml("""
            name: Loose
            futureFlag: yes
            jobs:
              - name: Build
                somethingElse: 12
                steps:
                  - command: echo hi
                    extra: true
            """);

        Assert.Equal("Loose", definition.Name);
        Assert.Single(definition.Jobs);
    }

    [Fact]
    public void Missing_check_name_falls_back_to_job_name()
    {
        var definition = PipelineYamlMapper.FromYaml(MinimalYaml(null));

        Assert.Null(definition.Jobs[0].CheckName);
        Assert.Equal("Build", definition.Jobs[0].EffectiveCheckName);
    }

    [Fact]
    public void ToCreateRequest_mirrors_the_json_contract()
    {
        var request = PipelineYamlMapper.ToCreateRequest(SampleYaml);

        Assert.Equal("My Pipeline", request.Name);
        Assert.Equal(3600, request.TimeoutSeconds);
        Assert.Equal([PipelineTrigger.Manual, PipelineTrigger.Push, PipelineTrigger.ChangeOpened], request.Triggers);
        Assert.Equal("value", request.Environment!["KEY"]);
        Assert.Single(request.Jobs);
    }

    [Fact]
    public void ToCreateRequest_propagates_validation_errors() =>
        Assert.Throws<ArgumentException>(() => PipelineYamlMapper.ToCreateRequest("name: Broken"));

    [Fact]
    public void Catalog_exposes_templates() =>
        Assert.NotEmpty(PipelineJobCatalog.Templates);

    [Fact]
    public void Catalog_template_ids_are_unique() =>
        Assert.Equal(
            PipelineJobCatalog.Templates.Count,
            PipelineJobCatalog.Templates.Select(template => template.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

    [Fact]
    public void Catalog_covers_the_documented_starter_set()
    {
        var ids = PipelineJobCatalog.Templates.Select(template => template.Id).ToArray();

        Assert.Contains("dotnet-build", ids);
        Assert.Contains("dotnet-test", ids);
        Assert.Contains("node-build", ids);
        Assert.Contains("docker-build", ids);
        Assert.Contains("shell-script", ids);
        Assert.Contains("publish-artifacts", ids);
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Catalog_templates_are_well_formed(string id)
    {
        var template = PipelineJobCatalog.Find(id);

        Assert.NotNull(template);
        Assert.False(string.IsNullOrWhiteSpace(template.Name));
        Assert.False(string.IsNullOrWhiteSpace(template.Description));
        Assert.False(string.IsNullOrWhiteSpace(template.Category));
        Assert.False(string.IsNullOrWhiteSpace(template.Icon));
        Assert.False(string.IsNullOrWhiteSpace(template.Job.Name));
        Assert.NotEmpty(template.Job.Steps);
        Assert.True(template.Job.TimeoutSeconds > 0);
        Assert.All(template.Job.Steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Name));
            Assert.False(string.IsNullOrWhiteSpace(step.Command));
            Assert.True(step.TimeoutSeconds > 0);
        });
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Catalog_templates_survive_a_yaml_round_trip(string id)
    {
        var template = PipelineJobCatalog.Find(id)!;
        var definition = new PipelineDefinition
        {
            Name = template.Name,
            Triggers = [PipelineTrigger.Manual],
            Jobs = [template.Job]
        };

        var restored = PipelineYamlMapper.FromYaml(PipelineYamlMapper.ToYaml(definition));

        AssertEquivalent(definition, restored);
    }

    [Theory]
    [InlineData("DOTNET-BUILD")]
    [InlineData("dotnet-build")]
    [InlineData(" dotnet-build ")]
    public void Catalog_lookup_is_case_and_whitespace_insensitive(string id) =>
        Assert.Equal("dotnet-build", PipelineJobCatalog.Find(id)?.Id);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("missing")]
    public void Catalog_lookup_returns_null_for_unknown_ids(string? id) =>
        Assert.Null(PipelineJobCatalog.Find(id));

    public static TheoryData<string> TemplateIds()
    {
        var data = new TheoryData<string>();
        foreach (var template in PipelineJobCatalog.Templates)
        {
            data.Add(template.Id);
        }

        return data;
    }

    private static string MinimalYaml(string? extra) => string.Join(
        '\n',
        [
            "name: Minimal",
            .. extra is null ? Array.Empty<string>() : [extra],
            "jobs:",
            "  - name: Build",
            "    steps:",
            "      - command: echo hi"
        ]);

    private static void AssertEquivalent(PipelineDefinition expected, PipelineDefinition actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Enabled, actual.Enabled);
        Assert.Equal(expected.TimeoutSeconds, actual.TimeoutSeconds);
        Assert.Equal(expected.Triggers, actual.Triggers);
        Assert.Equal(expected.Environment.OrderBy(pair => pair.Key), actual.Environment.OrderBy(pair => pair.Key));
        Assert.Equal(expected.Jobs.Count, actual.Jobs.Count);
        for (var i = 0; i < expected.Jobs.Count; i++)
        {
            var expectedJob = expected.Jobs[i];
            var actualJob = actual.Jobs[i];
            Assert.Equal(expectedJob.Name, actualJob.Name);
            Assert.Equal(expectedJob.RequiresCapabilities, actualJob.RequiresCapabilities);
            Assert.Equal(expectedJob.TimeoutSeconds, actualJob.TimeoutSeconds);
            Assert.Equal(expectedJob.PublishCheck, actualJob.PublishCheck);
            Assert.Equal(expectedJob.CheckName, actualJob.CheckName);
            Assert.Equal(expectedJob.ArtifactGlobs, actualJob.ArtifactGlobs);
            Assert.Equal(expectedJob.ContinueOnError, actualJob.ContinueOnError);
            Assert.Equal(expectedJob.Environment.OrderBy(pair => pair.Key), actualJob.Environment.OrderBy(pair => pair.Key));
            Assert.Equal(expectedJob.Steps.Count, actualJob.Steps.Count);
            for (var j = 0; j < expectedJob.Steps.Count; j++)
            {
                var expectedStep = expectedJob.Steps[j];
                var actualStep = actualJob.Steps[j];
                Assert.Equal(expectedStep.Name, actualStep.Name);
                Assert.Equal(expectedStep.Command, actualStep.Command);
                Assert.Equal(expectedStep.Shell, actualStep.Shell);
                Assert.Equal(expectedStep.TimeoutSeconds, actualStep.TimeoutSeconds);
                Assert.Equal(expectedStep.ContinueOnError, actualStep.ContinueOnError);
                Assert.Equal(expectedStep.Environment.OrderBy(pair => pair.Key), actualStep.Environment.OrderBy(pair => pair.Key));
            }
        }
    }
}
