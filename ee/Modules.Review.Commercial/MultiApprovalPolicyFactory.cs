using Modules.Review.Domain;
using Platform.Contracts.Capabilities;

namespace Modules.Review.Commercial;

public sealed class MultiApprovalPolicyFactory : IApprovalPolicyFactory
{
    public string RequiredCapability => KnownCapabilities.Review.MultiApproval;

    public bool CanHandle(ReviewPolicyState state) => state.MinimumApprovals > 1;

    public IApprovalPolicy Create(ReviewPolicyState state) =>
        new MinimumApprovalsPolicy(state.MinimumApprovals);
}
