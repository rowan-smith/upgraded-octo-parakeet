namespace ForgeDeck.Contracts.Onboarding;

/// <summary>
/// Deprecated. First-run onboarding is Core-only (Organisation → Licence → Owner → optional Members/Project).
/// Do not register module onboarding contributors for the setup wizard.
/// Prefer a future post-install configuration contributor for Module setup after Settings install.
/// </summary>
[Obsolete("First-run onboarding is Core-only. Module configuration happens after install.")]
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
