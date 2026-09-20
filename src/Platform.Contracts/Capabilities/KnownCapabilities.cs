namespace Platform.Contracts.Capabilities;

/// <summary>
/// Capability identifiers aligned with README licensing boundaries.
/// Community capabilities ship with installed modules; commercial capabilities require an organisation licence grant.
/// </summary>
public static class KnownCapabilities
{
    public static class Review
    {
        public const string BasicApproval = "Review.BasicApproval";
        public const string MultiApproval = "Review.MultiApproval";
        public const string TeamApproval = "Review.TeamApproval";
        public const string CodeOwners = "Review.CodeOwners";
        public const string PathPolicy = "Review.PathPolicy";
        public const string ConditionalPolicy = "Review.ConditionalPolicy";
        public const string PolicyComposition = "Review.PolicyComposition";
        public const string ReviewDismissal = "Review.ReviewDismissal";

        public static IReadOnlySet<string> Commercial { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MultiApproval, TeamApproval, CodeOwners, PathPolicy, ConditionalPolicy, PolicyComposition, ReviewDismissal
        };
    }

    public static class Deploy
    {
        public const string Approvals = "Deploy.Approvals";
        public const string MultiApproval = "Deploy.MultiApproval";
        public const string EnvironmentPolicy = "Deploy.EnvironmentPolicy";
        public const string PromotionPolicy = "Deploy.PromotionPolicy";
        public const string MultiSite = "Deploy.MultiSite";
        public const string AdvancedRollback = "Deploy.AdvancedRollback";
        public const string ChangeWindow = "Deploy.ChangeWindow";
        public const string ComplianceAudit = "Deploy.ComplianceAudit";

        public static IReadOnlySet<string> Commercial { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Approvals, MultiApproval, EnvironmentPolicy, PromotionPolicy, MultiSite, AdvancedRollback, ChangeWindow, ComplianceAudit
        };
    }

    public static IReadOnlySet<string> AllCommercial { get; } = new HashSet<string>(
        Review.Commercial.Concat(Deploy.Commercial), StringComparer.OrdinalIgnoreCase);

    public static bool IsCommercial(string capability) => AllCommercial.Contains(capability);
}
