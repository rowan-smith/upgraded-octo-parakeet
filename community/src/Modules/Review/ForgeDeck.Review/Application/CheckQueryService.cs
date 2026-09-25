using ForgeDeck.Contracts.Checks;

namespace ForgeDeck.Review.Application;

public sealed class CheckQueryService(IEnumerable<ICheckProvider> providers)
{
    public async Task<IReadOnlyList<CheckResult>> GetAsync(Guid changeId, CancellationToken token = default)
    {
        var results = await Task.WhenAll(providers.Select(provider => provider.GetChecksAsync(changeId, token)));
        return results.SelectMany(result => result).ToArray();
    }
}
