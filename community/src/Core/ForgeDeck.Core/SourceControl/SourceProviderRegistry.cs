using ForgeDeck.Contracts.SourceControl;
using ForgeDeck.Core.Extensions;

namespace ForgeDeck.Core.SourceControl;

public sealed class SourceProviderRegistry(
    IEnumerable<ISourceProvider> sourceProviders,
    IEnumerable<IChangeSourceProvider> changeProviders,
    IExtensionRegistry? extensions = null)
{
    public ISourceProvider Source(string id)
    {
        EnsureConnectorEnabled(id);
        return sourceProviders.FirstOrDefault(provider => provider.Id == id)
            ?? throw new InvalidOperationException($"Source provider '{id}' is not installed.");
    }

    public IChangeSourceProvider Changes(string id)
    {
        EnsureConnectorEnabled(id);
        return changeProviders.FirstOrDefault(provider => provider.Id == id)
            ?? throw new InvalidOperationException($"Change provider '{id}' is not installed.");
    }

    private void EnsureConnectorEnabled(string providerId)
    {
        if (extensions is null)
        {
            return;
        }

        var entry = BuiltinExtensionCatalogue.FindByRuntimeId(providerId);
        if (entry is null || entry.Type != ForgeDeck.Contracts.Extensions.ExtensionType.Connector)
        {
            return;
        }

        if (!extensions.IsEnabled(entry.ExtensionId))
        {
            throw new InvalidOperationException(
                $"Connector '{entry.Name}' is not enabled. Install it from Organisation Settings → Connectors.");
        }
    }
}
