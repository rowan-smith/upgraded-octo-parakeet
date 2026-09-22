using Platform.Contracts.Extensions;
using Platform.Contracts.Modules;

namespace Platform.Core.Extensions;

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
            "forgedeck.build", "Build", ExtensionType.Module, "0.2.0", "ForgeDeck",
            "CI pipelines, runs, jobs, tests, artifacts, and runners.",
            ["Pipelines", "Runs", "Jobs", "Tests", "Artifacts", "Runners"],
            "pipelines", ["check-provider", "pipelines"], [], [], Bundled: true),
        new(
            "forgedeck.deploy", "Deploy", ExtensionType.Module, "0.1.0", "ForgeDeck",
            "Release and deployment orchestration.",
            ["Releases", "Environments", "Deployments"],
            "deploy", ["deployment-provider"], [], [], Bundled: false),
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
