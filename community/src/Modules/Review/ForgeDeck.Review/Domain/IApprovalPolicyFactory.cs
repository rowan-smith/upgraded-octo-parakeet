namespace ForgeDeck.Review.Domain;

public interface IApprovalPolicyFactory
{
    string RequiredCapability { get; }
    bool CanHandle(ReviewPolicyState state);
    IApprovalPolicy Create(ReviewPolicyState state);
}
