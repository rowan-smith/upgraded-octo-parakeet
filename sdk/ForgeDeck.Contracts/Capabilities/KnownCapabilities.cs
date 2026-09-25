namespace ForgeDeck.Contracts.Capabilities;

/// <summary>
/// Capability identifiers aligned with the feature matrix (capability maturity).
/// Commercial capabilities require a signed licence grant and an installed commercial package.
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
        public const string SeparationOfDuties = "Review.SeparationOfDuties";
        public const string ComplianceExport = "Review.ComplianceExport";

        /// <summary>Team: coordinate reviews (multi-approval, CODEOWNERS, advanced workflow).</summary>
        public static IReadOnlySet<string> Team { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MultiApproval, TeamApproval, CodeOwners
        };

        /// <summary>Enterprise: govern reviews (policy, SoD, compliance evidence).</summary>
        public static IReadOnlySet<string> Enterprise { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PathPolicy, ConditionalPolicy, PolicyComposition, ReviewDismissal,
            SeparationOfDuties, ComplianceExport
        };

        public static IReadOnlySet<string> Commercial { get; } = new HashSet<string>(
            Team.Concat(Enterprise), StringComparer.OrdinalIgnoreCase);
    }

    public static class Build
    {
        public const string Concurrent = "Build.Concurrent";
        public const string Schedules = "Build.Schedules";
        public const string ProtectedSecrets = "Build.ProtectedSecrets";
        public const string SharedRunners = "Build.SharedRunners";
        public const string RunnerGroups = "Build.RunnerGroups";
        public const string Compliance = "Build.Compliance";
        public const string Approvals = "Build.Approvals";
        public const string Audit = "Build.Audit";
        public const string Attestation = "Build.Attestation";

        public static IReadOnlySet<string> Team { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Concurrent, Schedules, ProtectedSecrets, SharedRunners
        };

        public static IReadOnlySet<string> Enterprise { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RunnerGroups, Compliance, Approvals, Audit, Attestation
        };

        public static IReadOnlySet<string> Commercial { get; } = new HashSet<string>(
            Team.Concat(Enterprise), StringComparer.OrdinalIgnoreCase);
    }

    public static class Deploy
    {
        public const string MultiEnvironment = "Deploy.MultiEnvironment";
        public const string Approvals = "Deploy.Approvals";
        public const string MultiApproval = "Deploy.MultiApproval";
        public const string EnvironmentPolicy = "Deploy.EnvironmentPolicy";
        public const string PromotionPolicy = "Deploy.PromotionPolicy";
        public const string MultiSite = "Deploy.MultiSite";
        public const string AdvancedRollback = "Deploy.AdvancedRollback";
        public const string ChangeWindow = "Deploy.ChangeWindow";
        public const string ComplianceAudit = "Deploy.ComplianceAudit";

        public static IReadOnlySet<string> Team { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MultiEnvironment, Approvals, MultiApproval, EnvironmentPolicy, PromotionPolicy
        };

        public static IReadOnlySet<string> Enterprise { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MultiSite, AdvancedRollback, ChangeWindow, ComplianceAudit
        };

        public static IReadOnlySet<string> Commercial { get; } = new HashSet<string>(
            Team.Concat(Enterprise), StringComparer.OrdinalIgnoreCase);
    }

    public static class Git
    {
        public const string ProtectedBranches = "Git.ProtectedBranches";
        public const string PushRules = "Git.PushRules";
        public const string Mirror = "Git.Mirror";
        public const string GeoReplication = "Git.GeoReplication";

        public static IReadOnlySet<string> Team { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ProtectedBranches, PushRules
        };

        public static IReadOnlySet<string> Enterprise { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Mirror, GeoReplication
        };

        public static IReadOnlySet<string> Commercial { get; } = new HashSet<string>(
            Team.Concat(Enterprise), StringComparer.OrdinalIgnoreCase);
    }

    public static class Code
    {
        public const string AdvancedSearch = "Code.AdvancedSearch";
        public const string BlameInsights = "Code.BlameInsights";
        public const string SecurityScanning = "Code.SecurityScanning";
        public const string ComplianceViews = "Code.ComplianceViews";

        public static IReadOnlySet<string> Team { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AdvancedSearch, BlameInsights
        };

        public static IReadOnlySet<string> Enterprise { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SecurityScanning, ComplianceViews
        };

        public static IReadOnlySet<string> Commercial { get; } = new HashSet<string>(
            Team.Concat(Enterprise), StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlySet<string> AllCommercial { get; } = new HashSet<string>(
        Review.Commercial
            .Concat(Build.Commercial)
            .Concat(Deploy.Commercial)
            .Concat(Git.Commercial)
            .Concat(Code.Commercial),
        StringComparer.OrdinalIgnoreCase);

    public static bool IsCommercial(string capability) => AllCommercial.Contains(capability);
}
