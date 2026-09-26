namespace ForgeDeck.Core.Identity;

/// <summary>Dynamic registry of permission definitions contributed by Core and installed modules.</summary>
public interface IPermissionDefinitionRegistry
{
    IReadOnlyList<PermissionDescriptor> ListActive();
    IReadOnlyList<PermissionDescriptor> ListAll();
    PermissionDescriptor? Find(string permissionId);
    bool IsActive(string permissionId);
    void Register(PermissionDescriptor descriptor);
    void RegisterMany(IEnumerable<PermissionDescriptor> descriptors);
    void SetExtensionActive(string extensionId, bool active);
    IReadOnlySet<string> ActiveKeys();
}

public sealed class PermissionDefinitionRegistry : IPermissionDefinitionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PermissionDescriptor> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inactiveExtensions = new(StringComparer.OrdinalIgnoreCase);

    public PermissionDefinitionRegistry()
    {
        foreach (var descriptor in PermissionCatalogue.All)
        {
            _definitions[descriptor.Key] = descriptor;
        }
    }

    public IReadOnlyList<PermissionDescriptor> ListActive()
    {
        lock (_gate)
        {
            return _definitions.Values.Where(IsDefinitionActive).OrderBy(d => d.Category).ThenBy(d => d.Key).ToArray();
        }
    }

    public IReadOnlyList<PermissionDescriptor> ListAll()
    {
        lock (_gate)
        {
            return _definitions.Values.OrderBy(d => d.Category).ThenBy(d => d.Key).ToArray();
        }
    }

    public PermissionDescriptor? Find(string permissionId)
    {
        lock (_gate)
        {
            return _definitions.TryGetValue(permissionId, out var descriptor) ? descriptor : null;
        }
    }

    public bool IsActive(string permissionId)
    {
        lock (_gate)
        {
            return _definitions.TryGetValue(permissionId, out var descriptor) && IsDefinitionActive(descriptor);
        }
    }

    public void Register(PermissionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.Key))
        {
            throw new ArgumentException("Permission key is required.");
        }

        lock (_gate)
        {
            if (_definitions.TryGetValue(descriptor.Key, out var existing)
                && !DefinitionsCompatible(existing, descriptor))
            {
                throw new InvalidOperationException(
                    $"Permission '{descriptor.Key}' is already registered with an incompatible definition.");
            }

            _definitions[descriptor.Key] = descriptor;
        }
    }

    public void RegisterMany(IEnumerable<PermissionDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            Register(descriptor);
        }
    }

    public void SetExtensionActive(string extensionId, bool active)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        lock (_gate)
        {
            if (active)
            {
                _inactiveExtensions.Remove(extensionId);
            }
            else
            {
                _inactiveExtensions.Add(extensionId);
            }
        }
    }

    public IReadOnlySet<string> ActiveKeys()
    {
        lock (_gate)
        {
            return new HashSet<string>(
                _definitions.Values.Where(IsDefinitionActive).Select(d => d.Key),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private bool IsDefinitionActive(PermissionDescriptor descriptor) =>
        descriptor.SourceExtensionId is null
        || !_inactiveExtensions.Contains(descriptor.SourceExtensionId);

    private static bool DefinitionsCompatible(PermissionDescriptor existing, PermissionDescriptor incoming) =>
        string.Equals(existing.Key, incoming.Key, StringComparison.OrdinalIgnoreCase)
        && string.Equals(existing.Category, incoming.Category, StringComparison.OrdinalIgnoreCase)
        && existing.AllowedScopes.SequenceEqual(incoming.AllowedScopes)
        && string.Equals(existing.SourceExtensionId, incoming.SourceExtensionId, StringComparison.OrdinalIgnoreCase);
}
