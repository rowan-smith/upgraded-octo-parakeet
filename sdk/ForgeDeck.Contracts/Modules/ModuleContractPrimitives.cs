namespace ForgeDeck.Contracts.Modules;

/// <summary>How a module binary is hosted. First-party modules may stay InProcess; third parties should prefer OutOfProcess.</summary>
public enum ModuleRuntimeKind
{
    InProcess = 0,
    OutOfProcess = 1,
    Container = 2
}

/// <summary>Stable platform permission identifiers requested at install time.</summary>
public static class PlatformPermissions
{
    public const string ProjectRead = "project.read";
    public const string ProjectWrite = "project.write";
    public const string RepositoryRead = "repository.read";
    public const string RepositoryWrite = "repository.write";
    public const string CodeRead = "code.read";
    public const string ReviewRead = "review.read";
    public const string ReviewCreate = "review.create";
    public const string ReviewStatusWrite = "review.status.write";
    public const string BuildRead = "build.read";
    public const string BuildTrigger = "build.trigger";
    public const string BuildArtifactsRead = "build.artifacts.read";
    public const string DeployRead = "deploy.read";
    public const string DeployExecute = "deploy.execute";
    public const string UsersRead = "users.read";
    public const string TeamsRead = "teams.read";
    public const string SecretsRead = "secrets.read";
}

/// <summary>Named extension points modules may contribute to.</summary>
public static class ExtensionPoints
{
    public const string PlatformNavigation = "platform.navigation";
    public const string ProjectNavigation = "project.navigation";
    public const string ProjectSettings = "project.settings";
    public const string ReviewChecks = "review.checks";
    public const string ReviewTabs = "review.tabs";
    public const string ReviewMergeGates = "review.merge-gates";
    public const string BuildSteps = "build.steps";
    public const string BuildRunners = "build.runners";
    public const string DeployProviders = "deploy.providers";
    public const string DeployTargets = "deploy.targets";
    public const string DeployApprovalGates = "deploy.approval-gates";
}
