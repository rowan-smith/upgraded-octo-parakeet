using ForgeDeck.Contracts.Events;
using ForgeDeck.Review.Application;
using ForgeDeck.Review.Domain;

namespace ForgeDeck.Review.Application;

/// <summary>Records high-level pipeline outcomes on Change activity without referencing Pipelines.</summary>
public sealed class PipelineActivityHandler(IChangeRepository repository) :
    IEventHandler<PipelineRunStarted>,
    IEventHandler<PipelineRunCompleted>
{
    public Task HandleAsync(PipelineRunStarted domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent.ChangeId is not Guid changeId)
        {
            return Task.CompletedTask;
        }

        var change = repository.Find(changeId);
        if (change is null)
        {
            return Task.CompletedTask;
        }

        change.RecordActivity("PipelineStarted", "Pipelines", $"{domainEvent.PipelineName} started for {Short(domainEvent.CommitSha)}.");
        repository.Update(change);
        return Task.CompletedTask;
    }

    public Task HandleAsync(PipelineRunCompleted domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent.ChangeId is not Guid changeId)
        {
            return Task.CompletedTask;
        }

        var change = repository.Find(changeId);
        if (change is null)
        {
            return Task.CompletedTask;
        }

        var detail = domainEvent.Status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase)
            ? $"{domainEvent.PipelineName} passed for {Short(domainEvent.CommitSha)}."
            : domainEvent.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                ? $"{domainEvent.PipelineName} cancelled for {Short(domainEvent.CommitSha)}."
                : $"{domainEvent.PipelineName} failed for {Short(domainEvent.CommitSha)} ({domainEvent.Status}).";
        var type = domainEvent.Status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase) ? "PipelinePassed"
            : domainEvent.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ? "PipelineCancelled"
            : "PipelineFailed";
        change.RecordActivity(type, "Pipelines", detail);
        repository.Update(change);
        return Task.CompletedTask;
    }

    private static string Short(string sha) => sha.Length <= 7 ? sha : sha[..7];
}
