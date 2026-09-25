using ForgeDeck.Contracts.Checks;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.SourceControl;

namespace ForgeDeck.Core.Modules;

/// <summary>
/// Builds a provider catalogue from DI-registered contracts.
/// First-party and third-party providers appear the same way.
/// </summary>
public sealed class ProviderCatalogue : IProviderCatalogue
{
    public ProviderCatalogue(
        IEnumerable<IPlatformModule> modules,
        IEnumerable<ISourceProvider> sources,
        IEnumerable<IChangeSourceProvider> changeSources,
        IEnumerable<ICheckProvider> checks)
    {
        var list = new List<ProviderRegistration>();
        var moduleIds = modules.Select(m => m.Manifest.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            list.Add(new(GuessOwner(source.Id, source.GetType().Name, moduleIds, "git", "github"), source.Id, ProviderKind.Source, nameof(ISourceProvider)));
        }

        foreach (var change in changeSources)
        {
            list.Add(new(GuessOwner(change.Id, change.GetType().Name, moduleIds, "git", "github"), change.Id, ProviderKind.ChangeSource, nameof(IChangeSourceProvider)));
        }

        foreach (var check in checks)
        {
            var owner = GuessOwner(check.GetType().Name, check.GetType().Name, moduleIds, "pipelines", "checks-demo");
            list.Add(new(owner, check.GetType().Name, ProviderKind.Check, nameof(ICheckProvider)));
            if (owner.Equals("pipelines", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new(owner, check.GetType().Name, ProviderKind.BuildExecution, nameof(ICheckProvider)));
            }
        }

        All = list;
    }

    public IReadOnlyList<ProviderRegistration> All { get; }

    public IReadOnlyList<ProviderRegistration> OfKind(ProviderKind kind) =>
        All.Where(r => r.Kind == kind).ToArray();

    public bool Has(ProviderKind kind) => All.Any(r => r.Kind == kind);

    private static string GuessOwner(string providerId, string typeName, HashSet<string> moduleIds, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (moduleIds.Contains(candidate))
            {
                return candidate;
            }

            if (providerId.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            if (typeName.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return "unknown";
    }
}
