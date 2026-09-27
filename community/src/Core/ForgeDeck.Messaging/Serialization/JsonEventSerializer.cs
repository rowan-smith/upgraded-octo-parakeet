using System.Text.Json;
using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Messaging.Serialization;

public interface IEventSerializer
{
    JsonElement SerializePayload<TEvent>(TEvent data) where TEvent : class;
    string SerializeEnvelope(EventEnvelope envelope);
    EventEnvelope DeserializeEnvelope(string json);
    TEvent DeserializePayload<TEvent>(JsonElement data) where TEvent : class;
    object DeserializePayload(Type clrType, JsonElement data);
}

public sealed class JsonEventSerializer : IEventSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public JsonElement SerializePayload<TEvent>(TEvent data) where TEvent : class
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, Options);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    public string SerializeEnvelope(EventEnvelope envelope) =>
        JsonSerializer.Serialize(new EnvelopeDto(
            envelope.Id,
            envelope.Type,
            envelope.Version,
            envelope.OccurredAt,
            envelope.Actor,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.OrganisationId,
            envelope.ProjectId,
            envelope.Publisher,
            envelope.Data), Options);

    public EventEnvelope DeserializeEnvelope(string json)
    {
        var dto = JsonSerializer.Deserialize<EnvelopeDto>(json, Options)
                  ?? throw new InvalidOperationException("Invalid event envelope JSON.");
        return new EventEnvelope(
            dto.Id,
            dto.Type,
            dto.Version,
            dto.OccurredAt,
            dto.Actor,
            dto.CorrelationId,
            dto.CausationId,
            dto.OrganisationId,
            dto.ProjectId,
            dto.Publisher,
            dto.Data);
    }

    public TEvent DeserializePayload<TEvent>(JsonElement data) where TEvent : class =>
        data.Deserialize<TEvent>(Options)
        ?? throw new InvalidOperationException($"Unable to deserialize payload as {typeof(TEvent).Name}.");

    public object DeserializePayload(Type clrType, JsonElement data) =>
        data.Deserialize(clrType, Options)
        ?? throw new InvalidOperationException($"Unable to deserialize payload as {clrType.Name}.");

    private sealed record EnvelopeDto(
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
}
