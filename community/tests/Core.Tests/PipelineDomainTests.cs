using ForgeDeck.Build.Application;
using ForgeDeck.Build.Domain;
using ForgeDeck.Contracts.Artifacts;
using ForgeDeck.Contracts.Checks;

namespace Core.Tests;

public sealed class PipelineDomainTests
{
    [Fact]
    public void Runner_capability_matching_requires_all_advertised()
    {
        var runner = new RunnerAgent
        {
            Name = "pc",
            TokenHash = "x",
            Capabilities = ["dotnet", "docker"]
        };

        Assert.True(runner.Supports(["dotnet"]));
        Assert.True(runner.Supports(["dotnet", "docker"]));
        Assert.False(runner.Supports(["dotnet", "playwright"]));
        Assert.True(runner.Supports([]));
    }

    [Fact]
    public void Superseded_change_run_cancels_older_active_run()
    {
        var definition = SqlitePipelineStoreSeed();
        var first = PipelineRun.Create(definition, "{}", PipelineTrigger.ChangeOpened, "feature", "aaa");
        first.Start();
        var second = PipelineRun.Create(definition, "{}", PipelineTrigger.ChangeUpdated, "feature", "bbb");
        first.Supersede(second.Id);

        Assert.True(first.IsSuperseded);
        Assert.Equal(second.Id, first.SupersededByRunId);
        Assert.Equal(PipelineRunStatus.Cancelled, first.Status);
    }

    [Fact]
    public void Failed_required_job_skips_later_jobs()
    {
        var definition = new PipelineDefinition
        {
            Name = "seq",
            Triggers = [PipelineTrigger.Manual],
            Jobs =
            [
                new("One", [new("a", "exit 1", null, new Dictionary<string, string>(), 60)], [], new Dictionary<string, string>(), 60, true, "One", []),
                new("Two", [new("b", "echo ok", null, new Dictionary<string, string>(), 60)], [], new Dictionary<string, string>(), 60, true, "Two", [])
            ]
        };
        var run = PipelineRun.Create(definition, "{}", PipelineTrigger.Manual, "main", "abc");
        run.Start();
        run.Jobs[0].Assign(Guid.NewGuid());
        run.Jobs[0].MarkRunning();
        run.Jobs[0].Complete(JobStatus.Failed, 1, "boom", FailureKind.Pipeline);
        run.AdvanceAfterJobCompletion(run.Jobs[0].Id);

        Assert.Equal(JobStatus.Skipped, run.Jobs[1].Status);
        Assert.Equal(PipelineRunStatus.Failed, run.Status);
    }

    [Fact]
    public void Trx_parser_maps_outcomes()
    {
        const string trx = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" name="sample">
              <Results>
                <UnitTestResult testName="A.Pass" outcome="Passed" duration="00:00:00.0100000" />
                <UnitTestResult testName="A.Fail" outcome="Failed" duration="00:00:00.0200000">
                  <Output><ErrorInfo><Message>Expected true</Message><StackTrace>at A.Fail()</StackTrace></ErrorInfo></Output>
                </UnitTestResult>
                <UnitTestResult testName="A.Skip" outcome="NotExecuted" duration="00:00:00.0000000" />
              </Results>
            </TestRun>
            """;

        var results = TrxParser.Parse(trx);
        Assert.Equal(1, results.Passed);
        Assert.Equal(1, results.Failed);
        Assert.Equal(1, results.Skipped);
        Assert.Contains(results.Suites[0].Cases, c => c.Name == "A.Fail" && c.ErrorMessage!.Contains("Expected true"));
    }

    [Fact]
    public void Stale_sha_cannot_satisfy_required_check()
    {
        IReadOnlyList<CheckResult> checks =
        [
            new("Pipelines", "Build", CheckStatus.Passed, "1s", CommitSha: "old")
        ];
        Assert.False(RequiredCheckEvaluator.IsSatisfied(checks, "new", "Build"));
        Assert.True(RequiredCheckEvaluator.IsSatisfied(
            [new("Pipelines", "Build", CheckStatus.Passed, "1s", CommitSha: "new")], "new", "Build"));
    }

    [Fact]
    public void Cancel_acknowledge_filters_pending_notifications()
    {
        var runnerId = Guid.NewGuid();
        var job = PipelineJob.FromDefinition(
            new PipelineJobDefinition(
                "Build",
                [new PipelineStepDefinition("b", "echo", null, new Dictionary<string, string>(), 60)],
                [],
                new Dictionary<string, string>(),
                60,
                true,
                "Build",
                []),
            0);
        job.MarkQueued();
        job.MarkWaitingForRunner();
        job.Assign(runnerId);
        job.Cancel("stop");

        Assert.True(job.IsPendingCancelNotification(runnerId));
        Assert.False(job.IsPendingCancelNotification(Guid.NewGuid()));

        job.AcknowledgeCancel();
        Assert.True(job.CancelAcknowledged);
        Assert.False(job.IsPendingCancelNotification(runnerId));
    }

    [Fact]
    public void Cancel_notification_expires_after_ten_minutes()
    {
        var runnerId = Guid.NewGuid();
        var job = PipelineJob.FromDefinition(
            new PipelineJobDefinition(
                "Build",
                [new PipelineStepDefinition("b", "echo", null, new Dictionary<string, string>(), 60)],
                [],
                new Dictionary<string, string>(),
                60,
                true,
                "Build",
                []),
            0);
        job.MarkQueued();
        job.MarkWaitingForRunner();
        job.Assign(runnerId);
        job.Cancel("stop");

        Assert.True(job.IsPendingCancelNotification(runnerId, DateTimeOffset.UtcNow));
        Assert.False(job.IsPendingCancelNotification(runnerId, DateTimeOffset.UtcNow.AddMinutes(11)));
    }

    [Fact]
    public void Artifact_reference_supports_phase3_kinds()
    {
        var local = ArtifactReference.LocalFile("/tmp/a.trx", "application/xml");
        Assert.Equal("local-file", local.Kind);
        Assert.Equal("oci-image", ArtifactReference.OciImage("ghcr.io/org/app:1").Kind);
    }

    [Theory]
    [InlineData(PipelineTrigger.Manual)]
    [InlineData(PipelineTrigger.Push)]
    [InlineData(PipelineTrigger.ChangeOpened)]
    [InlineData(PipelineTrigger.ChangeUpdated)]
    public void Pipeline_run_records_trigger(PipelineTrigger trigger)
    {
        var definition = SqlitePipelineStoreSeed();
        var run = PipelineRun.Create(definition, "{}", trigger, "main", "abc1234");
        Assert.Equal(trigger, run.Trigger);
        Assert.Equal(PipelineRunStatus.Queued, run.Status);
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("docker")]
    [InlineData("node")]
    [InlineData("python")]
    [InlineData("playwright")]
    public void Runner_supports_single_capability_when_advertised(string capability)
    {
        var runner = new RunnerAgent
        {
            Name = "agent",
            TokenHash = "x",
            Capabilities = [capability, "extra"]
        };
        Assert.True(runner.Supports([capability]));
        Assert.False(runner.Supports([capability, "missing"]));
    }

    [Theory]
    [InlineData(JobStatus.Succeeded)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public void Job_completion_sets_terminal_status(JobStatus status)
    {
        var job = PipelineJob.FromDefinition(
            new PipelineJobDefinition(
                "Build",
                [new PipelineStepDefinition("b", "dotnet build", null, new Dictionary<string, string>(), 60)],
                ["dotnet"],
                new Dictionary<string, string>(),
                60,
                true,
                "Build",
                []),
            0);
        job.MarkQueued();
        job.MarkWaitingForRunner();
        job.Assign(Guid.NewGuid());
        job.MarkRunning();
        if (status == JobStatus.Succeeded)
        {
            job.Complete(JobStatus.Succeeded, 0, null, FailureKind.None);
        }
        else if (status == JobStatus.Failed)
        {
            job.Complete(JobStatus.Failed, 1, "boom", FailureKind.Pipeline);
        }
        else
        {
            job.Cancel("stop");
        }

        Assert.Equal(status, job.Status);
    }

    private static PipelineDefinition SqlitePipelineStoreSeed() => new()
    {
        Name = "seed",
        Triggers = [PipelineTrigger.Manual],
        Jobs =
        [
            new("Build", [new("b", "dotnet build", null, new Dictionary<string, string>(), 60)], ["dotnet"],
                new Dictionary<string, string>(), 60, true, "Build", [])
        ]
    };
}
