using Platform.Contracts.Audit;

namespace Platform.Core.Audit;

public sealed class AuditStore
{
    private readonly List<AuditEvent> _events = [];
    private readonly object _gate = new();

    public void Append(AuditEvent auditEvent) { lock (_gate) _events.Insert(0, auditEvent); }
    public IReadOnlyList<AuditEvent> Snapshot() { lock (_gate) return _events.ToArray(); }
}
