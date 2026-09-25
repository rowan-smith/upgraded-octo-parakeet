using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Context;

namespace ForgeDeck.Review.Domain;

public sealed class ReviewPolicyState
{
    public int MinimumApprovals { get; set; } = 1;
}

public sealed class ApprovalPolicyResolver(
    ICapabilityService capabilities,
    PlatformContextStore context,
    ReviewPolicyState state,
    IEnumerable<IApprovalPolicyFactory> factories)
{
    public ReviewPolicySnapshot Snapshot()
    {
        var multiApproval = capabilities.Has(context.Organisation.Id, KnownCapabilities.Review.MultiApproval);
        return new(state.MinimumApprovals, multiApproval, Resolve().GetType().Name);
    }

    public IApprovalPolicy Resolve()
    {
        foreach (var factory in factories)
        {
            if (!factory.CanHandle(state))
            {
                continue;
            }

            if (!capabilities.Has(context.Organisation.Id, factory.RequiredCapability))
            {
                continue;
            }

            return factory.Create(state);
        }

        return new SingleApprovalPolicy();
    }

    public void Configure(int minimumApprovals, CapabilityAuthorizer authorizer)
    {
        // Community: a single approval rule (e.g. "main requires 1 approval"). Multi-rule / N>1 needs Team.
        var minimum = Math.Max(1, minimumApprovals);
        if (minimum > CommunityLimits.ReviewMaxApprovalRules)
        {
            authorizer.Ensure(KnownCapabilities.Review.MultiApproval);
        }

        state.MinimumApprovals = minimum;
    }
}

public sealed record ReviewPolicySnapshot(int MinimumApprovals, bool MultiApprovalLicensed, string ActivePolicy);
