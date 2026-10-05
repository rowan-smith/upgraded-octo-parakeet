using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Review.Application;

public sealed class PipelineActivityHandler(IChangeRepository repository) : IEventHandler<BuildPipelineRunStartedEvent>
{
    public string ConsumerId => "forgedeck.review.pipeline-started";

    public Task HandleAsync(EventEnvelope<BuildPipelineRunStartedEvent> envelope, CancellationToken cancellationToken = default)
    {
        var domainEvent = envelope.Data;
        if (domainEvent.ChangeId is not Guid changeId)
        {
            return Task.CompletedTask;
        }

        repository.Mutate(changeId, change =>
            change.RecordActivity("PipelineStarted", "Pipelines", $"{domainEvent.PipelineName} started for {Short(domainEvent.CommitSha)}."));
        return Task.CompletedTask;
    }

    private static string Short(string sha) => sha.Length <= 7 ? sha : sha[..7];
}

public sealed class PipelineSucceededActivityHandler(IChangeRepository repository) : IEventHandler<BuildPipelineRunSucceededEvent>
{
    public string ConsumerId => "forgedeck.review.pipeline-succeeded";

    public Task HandleAsync(EventEnvelope<BuildPipelineRunSucceededEvent> envelope, CancellationToken cancellationToken = default)
    {
        var e = envelope.Data;
        if (e.ChangeId is not Guid changeId)
        {
            return Task.CompletedTask;
        }

        repository.Mutate(changeId, change =>
            change.RecordActivity("PipelinePassed", "Pipelines", $"{e.PipelineName} passed for {Short(e.CommitSha)}."));
        return Task.CompletedTask;
    }

    private static string Short(string sha) => sha.Length <= 7 ? sha : sha[..7];
}

public sealed class PipelineFailedActivityHandler(IChangeRepository repository) : IEventHandler<BuildPipelineRunFailedEvent>
{
    public string ConsumerId => "forgedeck.review.pipeline-failed";

    public Task HandleAsync(EventEnvelope<BuildPipelineRunFailedEvent> envelope, CancellationToken cancellationToken = default)
    {
        var e = envelope.Data;
        if (e.ChangeId is not Guid changeId)
        {
            return Task.CompletedTask;
        }

        repository.Mutate(changeId, change =>
            change.RecordActivity("PipelineFailed", "Pipelines", $"{e.PipelineName} failed for {Short(e.CommitSha)}."));
        return Task.CompletedTask;
    }

    private static string Short(string sha) => sha.Length <= 7 ? sha : sha[..7];
}

public sealed class PipelineCancelledActivityHandler(IChangeRepository repository) : IEventHandler<BuildPipelineRunCancelledEvent>
{
    public string ConsumerId => "forgedeck.review.pipeline-cancelled";

    public Task HandleAsync(EventEnvelope<BuildPipelineRunCancelledEvent> envelope, CancellationToken cancellationToken = default)
    {
        var e = envelope.Data;
        if (e.ChangeId is not Guid changeId)
        {
            return Task.CompletedTask;
        }

        repository.Mutate(changeId, change =>
            change.RecordActivity("PipelineCancelled", "Pipelines", $"{e.PipelineName} cancelled for {Short(e.CommitSha)}."));
        return Task.CompletedTask;
    }

    private static string Short(string sha) => sha.Length <= 7 ? sha : sha[..7];
}
