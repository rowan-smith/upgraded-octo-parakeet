using Platform.Contracts.Capabilities;
using Platform.Contracts.Modules;

namespace Platform.Core.Capabilities;

public sealed class CapabilityService(IEnumerable<IPlatformModule> modules) : ICapabilityService
{
    private readonly HashSet<string> _capabilities = modules.SelectMany(m => m.Manifest.Capabilities).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public bool Has(string capability) => _capabilities.Contains(capability);
    public IReadOnlySet<string> Current => _capabilities;
}
