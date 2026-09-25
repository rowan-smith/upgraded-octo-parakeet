namespace ForgeDeck.Review.Domain;

public interface IApprovalPolicy
{
    ApprovalPolicyResult Evaluate(Change change);
}
