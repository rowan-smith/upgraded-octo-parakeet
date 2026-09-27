using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Deploy.Application;
using ForgeDeck.Deploy.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Core.Tests;

public sealed class DeployBuildEventWiringTests
{
    [Fact]
    public async Task Pipeline_succeeded_handler_persists_build_run_reference()
    {
        using var harness = CreateHarness();
        var runId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var changeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var handler = new PipelineSucceededDeployHandler(harness.Store);

        await handler.HandleAsync(Envelope(new BuildPipelineRunSucceededEvent(
            runId,
            changeId,
            ".NET Validation",
            "abc1234")));

        var refs = harness.Store.ListBuildRunReferences();
        var match = Assert.Single(refs);
        Assert.Equal(runId, match.RunId);
        Assert.Equal(".NET Validation", match.PipelineName);
        Assert.Equal("abc1234", match.CommitSha);
        Assert.Equal(changeId, match.ChangeId);
        Assert.Equal("forgedeck.deploy.pipeline-succeeded", handler.ConsumerId);
    }

    [Fact]
    public async Task Artifact_produced_handler_persists_artifact_reference()
    {
        using var harness = CreateHarness();
        var runId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var handler = new BuildArtifactAvailableHandler(harness.Store);

        await handler.HandleAsync(Envelope(new BuildArtifactProducedEvent(
            runId,
            ".NET Validation",
            "web-dist",
            "artifacts/web-dist.zip")));

        var refs = harness.Store.ListArtifactReferences(runId);
        var match = Assert.Single(refs);
        Assert.Equal(runId, match.RunId);
        Assert.Equal(".NET Validation", match.PipelineName);
        Assert.Equal("web-dist", match.ArtifactName);
        Assert.Equal("artifacts/web-dist.zip", match.Uri);
        Assert.Equal("forgedeck.deploy.artifact-produced", handler.ConsumerId);
    }

    [Fact]
    public async Task Handlers_do_not_create_deployments()
    {
        using var harness = CreateHarness();
        var runId = Guid.NewGuid();

        await new PipelineSucceededDeployHandler(harness.Store).HandleAsync(
            Envelope(new BuildPipelineRunSucceededEvent(runId, null, "ci", "deadbee")));
        await new BuildArtifactAvailableHandler(harness.Store).HandleAsync(
            Envelope(new BuildArtifactProducedEvent(runId, "ci", "bin", null)));

        Assert.Empty(harness.Store.ListDeployments());
        Assert.Single(harness.Store.ListBuildRunReferences());
        Assert.Single(harness.Store.ListArtifactReferences(runId));
    }

    private static EventEnvelope<T> Envelope<T>(T data) where T : class =>
        new(
            Guid.NewGuid(),
            "test",
            1,
            DateTimeOffset.UtcNow,
            new EventActor(ActorType.System, "test", "test"),
            Guid.NewGuid(),
            null,
            null,
            null,
            "test",
            data);

    private static Harness CreateHarness()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forgedeck-deploy-build-{Guid.NewGuid():N}.db");
        var factory = new TestDeployDbContextFactory($"Data Source={path}");
        var store = new EfDeployStore(factory);
        return new Harness(path, store);
    }

    private sealed class TestDeployDbContextFactory(string connectionString) : IDbContextFactory<DeployDbContext>
    {
        public DeployDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<DeployDbContext>().UseSqlite(connectionString).Options;
            return new DeployDbContext(options);
        }
    }

    private sealed class Harness(string path, EfDeployStore store) : IDisposable
    {
        public EfDeployStore Store { get; } = store;

        public void Dispose()
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
