using Platform.Contracts.SourceControl;

namespace Platform.Core.SourceControl;

public sealed class SourceProviderRegistry(IEnumerable<ISourceProvider> sourceProviders, IEnumerable<IChangeSourceProvider> changeProviders)
{
    public ISourceProvider Source(string id) => sourceProviders.FirstOrDefault(provider => provider.Id == id)
        ?? throw new InvalidOperationException($"Source provider '{id}' is not installed.");
    public IChangeSourceProvider Changes(string id) => changeProviders.FirstOrDefault(provider => provider.Id == id)
        ?? throw new InvalidOperationException($"Change provider '{id}' is not installed.");
}
