using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Contracts.Checks;

/// <summary>
/// Provider-neutral check status change. Prefer this over Build-specific success events
/// when projecting checks into Review or other consumers.
/// </summary>
public sealed record CheckUpdatedEvent(
    Guid? ChangeId,
    string Provider,
    string Name,
    CheckStatus Status,
    string CommitSha,
    string? DetailsUrl = null,
    string? Summary = null,
    Guid? RunId = null);

public static class CheckUpdatedEventContract
{
    public const string Type = "forgedeck.checks.updated";
    public const int Version = 1;

    public static EventContract<CheckUpdatedEvent> Create() => new(Type, Version);
}
