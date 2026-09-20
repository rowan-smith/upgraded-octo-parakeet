namespace Platform.Contracts.Events;

public interface IDomainEvent
{
    Guid EventId { get; }
    int Version { get; }
    DateTimeOffset OccurredAt { get; }
    string CorrelationId { get; }
}

public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;
}

public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}

public sealed record ChangeOpened(
    Guid ChangeId,
    string ProjectKey,
    string Repository,
    string SourceBranch,
    string TargetBranch,
    string CommitSha) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public int Version => 1;
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    public string CorrelationId { get; } = Guid.NewGuid().ToString("N");
}

public sealed record PushReceived(
    Guid RepositoryId,
    string ProjectKey,
    string Repository,
    string Branch,
    string CommitSha) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public int Version => 1;
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    public string CorrelationId { get; } = Guid.NewGuid().ToString("N");
}
