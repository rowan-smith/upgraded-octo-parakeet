namespace ForgeDeck.Contracts.Audit;

public sealed record AuditEvent(Guid Id, string Actor, string Organisation, string Project, string Module,
    string Action, string Resource, DateTimeOffset Timestamp, string CorrelationId, object? Metadata);
