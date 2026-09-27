using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Messaging.Registry;

public sealed class EventRegistry : IEventRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Type, int Version), IEventContract> _byTypeVersion = new(StringTupleComparer.Instance);
    private readonly Dictionary<Type, IEventContract> _byClr = new();

    public void Register(IEventContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        lock (_gate)
        {
            var key = (contract.Type, contract.Version);
            if (_byTypeVersion.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Event contract '{contract.Type}' v{contract.Version} is already registered.");
            }

            _byTypeVersion[key] = contract;
            _byClr[contract.ClrType] = contract;
        }
    }

    public IEventContract GetRequired(string type, int version) =>
        TryGet(type, version) ?? throw new InvalidOperationException($"Unknown event contract '{type}' v{version}.");

    public IEventContract? TryGet(string type, int version)
    {
        lock (_gate)
        {
            return _byTypeVersion.TryGetValue((type, version), out var contract) ? contract : null;
        }
    }

    public IEventContract? TryGetByClrType(Type clrType)
    {
        lock (_gate)
        {
            return _byClr.TryGetValue(clrType, out var contract) ? contract : null;
        }
    }

    public IReadOnlyList<IEventContract> List()
    {
        lock (_gate)
        {
            return _byTypeVersion.Values.OrderBy(c => c.Type).ThenBy(c => c.Version).ToArray();
        }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Type, int Version)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((string Type, int Version) x, (string Type, int Version) y) =>
            x.Version == y.Version && string.Equals(x.Type, y.Type, StringComparison.Ordinal);
        public int GetHashCode((string Type, int Version) obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Type), obj.Version);
    }
}
