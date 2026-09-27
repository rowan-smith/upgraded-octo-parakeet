namespace ForgeDeck.Contracts.Events;

/// <summary>
/// Ambient correlation/causation for nested publishes while a handler processes an event.
/// </summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<Scope?> CurrentScope = new();

    public static EventEnvelope? Current => CurrentScope.Value?.Envelope;

    public static IDisposable Push(EventEnvelope envelope)
    {
        var scope = new Scope(envelope, CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    private sealed class Scope(EventEnvelope envelope, Scope? parent) : IDisposable
    {
        public EventEnvelope Envelope { get; } = envelope;

        public void Dispose()
        {
            if (ReferenceEquals(CurrentScope.Value, this))
            {
                CurrentScope.Value = parent;
            }
        }
    }
}
