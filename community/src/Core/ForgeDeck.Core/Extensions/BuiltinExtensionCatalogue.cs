using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Contracts.Modules;

namespace ForgeDeck.Core.Extensions;

public static class BuiltinExtensionCatalogue
{
    public static IReadOnlyList<ExtensionCatalogueEntry> All { get; } =
    [
        new(
            "forgedeck.code", "Code", ExtensionType.Module, "1.0.0", "ForgeDeck",
            "Browse and understand source repositories.",
            ["Files", "Branches", "Commits", "Tags", "Git Graph"],
            "code", ["source-browser"], ["source-provider"], [], Bundled: true),
        new(
            "forgedeck.git", "Git", ExtensionType.Module, "0.3.0", "ForgeDeck",
            "Native ForgeDeck repository hosting and source provider.",
            ["Native repositories", "Source provider"],
            "git", ["source-provider"], [], [], Bundled: true),
        new(
            "forgedeck.review", "Review", ExtensionType.Module, "0.3.0", "ForgeDeck",
            "Review and approve code changes.",
            ["Pull Requests", "Inline Discussions", "Approvals", "Review Queue"],
            "review", ["review"], ["source-provider"], ["check-provider"], Bundled: true),
        new(
            "forgedeck.review.team", "Review Team", ExtensionType.Module, "0.1.0", "ForgeDeck",
            "Team review policies: multiple approvals, team approval, and CODEOWNERS.",
            ["Multiple Approvals", "Team Approval", "CODEOWNERS"],
            "review-team", ["review-team"], ["review"], [], Bundled: false),
        new(
            "forgedeck.review.enterprise", "Review Enterprise", ExtensionType.Module, "0.1.0", "ForgeDeck",
            "Enterprise review governance: path policies, SoD, and compliance evidence.",
            ["Path Policies", "Separation of Duties", "Compliance Export"],
            "review-enterprise", ["review-enterprise"], ["review", "review-team"], [], Bundled: false),
        // Migration alias for former review-commercial runtime id (maps to Team package).
        new(
            "forgedeck.review.commercial", "Review Commercial (alias)", ExtensionType.Module, "0.1.0", "ForgeDeck",
            "Deprecated alias of Review Team. Prefer forgedeck.review.team.",
            ["Multiple Approvals", "Team Approval"],
            "review-team", ["review-commercial", "review-team"], ["review"], [], Bundled: false),
        new(
            "forgedeck.build", "Build", ExtensionType.Module, "0.2.0", "ForgeDeck",
            "CI pipelines, runs, jobs, tests, artifacts, and runners.",
            ["Pipelines", "Runs", "Jobs", "Tests", "Artifacts", "Runners"],
            "pipelines", ["check-provider", "pipelines"], [], [], Bundled: true),
        new(
            "forgedeck.build.team", "Build Team", ExtensionType.Module, "0.1.0", "ForgeDeck",
            "Team build: concurrent pipelines, schedules, shared runners, protected secrets.",
            ["Concurrent Pipelines", "Schedules", "Shared Runners", "Protected Secrets"],
            "build-team", ["build-team"], ["pipelines"], [], Bundled: false),
        new(
            "forgedeck.build.enterprise", "Build Enterprise", ExtensionType.Module, "0.1.0", "ForgeDeck",
            "Enterprise build governance: runner groups, attestation, compliance.",
            ["Runner Groups", "Attestation", "Build Approvals", "Compliance"],
            "build-enterprise", ["build-enterprise"], ["pipelines", "build-team"], [], Bundled: false),
        new(
            "forgedeck.deploy", "Deploy", ExtensionType.Module, "0.1.0", "ForgeDeck",
            "Release and deployment orchestration.",
            ["Environments", "Deployments", "Rollback"],
            "deploy", ["deployment-provider", "deploy"], [], [], Bundled: true),
        new(
            "forgedeck.deploy.team", "Deploy Team", ExtensionType.Module, "0.1.0", "ForgeDeck",
            "Team deploy: multi-environment, promotions, environment policies.",
            ["Multi-Environment", "Promotion Policy", "Deploy Approvals"],
            "deploy-team", ["deploy-team"], ["deploy"], [], Bundled: false),
        new(
            "forgedeck.deploy.enterprise", "Deploy Enterprise", ExtensionType.Module, "0.1.0", "ForgeDeck",
            "Enterprise deploy: multi-site agents, change windows, compliance audit.",
            ["Multi-Site", "Change Windows", "Advanced Rollback", "Compliance Audit"],
            "deploy-enterprise", ["deploy-enterprise"], ["deploy", "deploy-team"], [], Bundled: false),
        new(
            "forgedeck.github", "GitHub", ExtensionType.Connector, "1.0.0", "ForgeDeck",
            "Connect GitHub repositories as a source and change provider.",
            ["Source provider", "Change provider", "Webhooks"],
            "github", ["source-provider", "change-provider"], [], [], Bundled: true),
        new(
            "forgedeck.gitlab", "GitLab", ExtensionType.Connector, "0.1.0", "ForgeDeck",
            "Connect GitLab projects as an external source provider.",
            ["Source provider"],
            "gitlab", ["source-provider"], [], [], Bundled: false),
        new(
            "forgedeck.azuredevops", "Azure DevOps", ExtensionType.Connector, "0.1.0", "ForgeDeck",
            "Connect Azure DevOps repositories and pipelines.",
            ["Source provider"],
            "azuredevops", ["source-provider"], [], [], Bundled: false),
        new(
            "forgedeck.jira", "Jira", ExtensionType.Connector, "0.1.0", "ForgeDeck",
            "Link work items and issues from Jira.",
            ["Issue tracking"],
            "jira", ["issue-provider"], [], [], Bundled: false),
        new(
            "forgedeck.teams", "Microsoft Teams", ExtensionType.Connector, "0.1.0", "ForgeDeck",
            "Notify channels when reviews and builds change.",
            ["Notifications"],
            "teams", ["notification-provider"], [], [], Bundled: false),
        new(
            "forgedeck.slack", "Slack", ExtensionType.Connector, "0.1.0", "ForgeDeck",
            "Notify Slack workspaces from ForgeDeck events.",
            ["Notifications"],
            "slack", ["notification-provider"], [], [], Bundled: false),
        new(
            "forgedeck.vault", "HashiCorp Vault", ExtensionType.Connector, "0.1.0", "ForgeDeck",
            "Retrieve secrets from HashiCorp Vault.",
            ["Secrets"],
            "vault", ["secrets-provider"], [], [], Bundled: false)
    ];

    public static ExtensionCatalogueEntry? Find(string extensionId) =>
        All.FirstOrDefault(e => e.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));

    public static ExtensionCatalogueEntry? FindByRuntimeId(string runtimeId) =>
        All.FirstOrDefault(e => e.RuntimeId is not null &&
            e.RuntimeId.Equals(runtimeId, StringComparison.OrdinalIgnoreCase));

    public static string? ExtensionIdForRuntime(string runtimeId) => FindByRuntimeId(runtimeId)?.ExtensionId;

    /// <summary>Map IPlatformModule.Manifest.Id to catalogue extension id.</summary>
    public static string? ExtensionIdForModule(ModuleManifest manifest) =>
        FindByRuntimeId(manifest.Id)?.ExtensionId;
}
