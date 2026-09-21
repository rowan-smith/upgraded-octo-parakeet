namespace Platform.Contracts.Onboarding;

/// <summary>Modules register onboarding steps evaluated after Owner + Project exist.</summary>
public interface IOnboardingContributor
{
    string Id { get; }
    string Title { get; }
    int Order { get; }
    bool IsRequired { get; }

    /// <summary>Capability required to show this step; null means always when module is installed.</summary>
    string? RequiredCapability { get; }

    Task<bool> IsCompleteAsync(IServiceProvider services, CancellationToken cancellationToken = default);
}

public sealed record OnboardingStepView(
    string Id,
    string Title,
    int Order,
    bool IsRequired,
    bool IsComplete,
    bool IsAvailable);
