using Microsoft.Extensions.DependencyInjection;
using Modules.Review.Domain;
using Platform.Contracts.Onboarding;
using Platform.Core.Persistence;

namespace Modules.Review;

public sealed class ReviewOnboardingContributor : IOnboardingContributor
{
    public string Id => "review-policy";
    public string Title => "Configure Review";
    public int Order => 10;
    public bool IsRequired => false;
    public string? RequiredCapability => null;

    public Task<bool> IsCompleteAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        // Treat any persisted policy state as configured once a project exists.
        var store = services.GetRequiredService<ITenancyStore>();
        return Task.FromResult(store.ListProjects().Count > 0 && services.GetService<ReviewPolicyState>() is not null);
    }
}
