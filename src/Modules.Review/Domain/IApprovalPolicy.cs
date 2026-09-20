namespace Modules.Review.Domain;

public interface IApprovalPolicy
{
    ApprovalPolicyResult Evaluate(Change change);
}
