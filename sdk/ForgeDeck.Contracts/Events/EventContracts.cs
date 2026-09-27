using System.Text.Json;

namespace ForgeDeck.Contracts.Events;

public enum ActorType
{
    User,
    System,
    Extension,
    External
}

public sealed record EventActor(
    ActorType Type,
    string? Id,
    string? DisplayName);

public sealed record EventEnvelope(
    Guid Id,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    EventActor Actor,
    Guid CorrelationId,
    Guid? CausationId,
    Guid? OrganisationId,
    Guid? ProjectId,
    string Publisher,
    JsonElement Data);

public sealed record EventEnvelope<TEvent>(
    Guid Id,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    EventActor Actor,
    Guid CorrelationId,
    Guid? CausationId,
    Guid? OrganisationId,
    Guid? ProjectId,
    string Publisher,
    TEvent Data)
{
    public EventEnvelope ToUntyped(JsonElement data) =>
        new(Id, Type, Version, OccurredAt, Actor, CorrelationId, CausationId, OrganisationId, ProjectId, Publisher, data);
}

public interface IEventContract
{
    string Type { get; }
    int Version { get; }
    Type ClrType { get; }
}

public sealed class EventContract<TEvent> : IEventContract where TEvent : class
{
    public EventContract(string type, int version)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Event type is required.", nameof(type));
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Event contract version must be > 0.");
        }

        Type = type;
        Version = version;
    }

    public string Type { get; }
    public int Version { get; }
    public Type ClrType => typeof(TEvent);
}

public sealed class PublishOptions
{
    public EventActor Actor { get; init; } = new(ActorType.System, "forgedeck", "ForgeDeck");
    public string Publisher { get; init; } = "forgedeck";
    public Guid? OrganisationId { get; init; }
    public Guid? ProjectId { get; init; }
    public bool Durable { get; init; } = true;
}

public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent data, PublishOptions? options = null, CancellationToken cancellationToken = default)
        where TEvent : class;
}

public interface IEventHandler<TEvent> where TEvent : class
{
    string ConsumerId { get; }
    Task HandleAsync(EventEnvelope<TEvent> envelope, CancellationToken cancellationToken = default);
}

public interface IEventSubscriber
{
    void Subscribe<TEvent>(string consumerId, IEventHandler<TEvent> handler) where TEvent : class;
    void Unsubscribe(string consumerId);
    IReadOnlyList<EventSubscriptionInfo> ListSubscriptions();
}

public sealed record EventSubscriptionInfo(
    string ConsumerId,
    string EventType,
    int? EventVersion,
    Type HandlerType,
    bool Active);

public interface IEventBus
{
    Task DispatchAsync(EventEnvelope envelope, CancellationToken cancellationToken = default);
}

public interface IEventRegistry
{
    void Register(IEventContract contract);
    IEventContract GetRequired(string type, int version);
    IEventContract? TryGet(string type, int version);
    IEventContract? TryGetByClrType(Type clrType);
    IReadOnlyList<IEventContract> List();
}

public interface IEventDiagnostics
{
    Task<IReadOnlyList<OutboxRecord>> ListRecentOutboxAsync(int take = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryRecord>> ListFailedDeliveriesAsync(int take = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventSubscriptionInfo>> ListSubscriptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboxRecord>> ListOutboxBacklogAsync(CancellationToken cancellationToken = default);
    Task<CorrelationTrace?> GetCorrelationTraceAsync(Guid correlationId, CancellationToken cancellationToken = default);
    Task RetryDeliveryAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default);
    Task AcknowledgeDeliveryAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default);
}

public enum OutboxDispatchState
{
    Pending,
    Dispatching,
    Dispatched,
    Failed
}

public enum DeliveryState
{
    Pending,
    Processing,
    Processed,
    Failed,
    DeadLetter,
    Cancelled
}

public sealed record OutboxRecord(
    Guid EventId,
    string EventType,
    int Version,
    string EnvelopeJson,
    string PayloadJson,
    DateTimeOffset CreatedAt,
    OutboxDispatchState DispatchState,
    int Attempts,
    DateTimeOffset? LastAttemptAt,
    string? LastError,
    DateTimeOffset? NextAttemptAt);

public sealed record DeliveryRecord(
    Guid EventId,
    string ConsumerId,
    string EventType,
    int Version,
    DeliveryState State,
    int Attempts,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    string? LastError,
    Guid CorrelationId,
    DateTimeOffset CreatedAt);

public sealed record CorrelationTrace(
    Guid CorrelationId,
    IReadOnlyList<CorrelationTraceNode> Nodes);

public sealed record CorrelationTraceNode(
    Guid EventId,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    Guid? CausationId,
    string Publisher);
