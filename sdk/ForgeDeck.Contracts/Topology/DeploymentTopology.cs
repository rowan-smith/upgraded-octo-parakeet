namespace ForgeDeck.Contracts.Topology;

/// <summary>
/// Licensing entitlements must not dictate process topology.
/// Split deployables by scale, security, and failure isolation.
/// </summary>
public static class DeploymentDomains
{
    public const string PlatformCore = "platform-core";
    /// <summary>Git + Code + Review — separately entitled, often one deployable initially.</summary>
    public const string Scm = "scm";
    public const string Build = "build";
    public const string Deploy = "deploy";
}

public static class DeploymentProfiles
{
    /// <summary>Single box: Core + SCM + Build + Deploy.</summary>
    public const string Appliance = "appliance";

    /// <summary>Core/SCM on one host; Build (+ runners) and Deploy on others.</summary>
    public const string TeamSplit = "team-split";

    /// <summary>HA Core, clustered Git, Build controller + pools, Deploy controller + site agents.</summary>
    public const string Enterprise = "enterprise";
}

/// <summary>
/// Logical database ownership. Modules must not query another module's schema —
/// even when databases share a PostgreSQL/SQLite server instance.
/// </summary>
public static class ModuleDatabases
{
    public const string Platform = "platform";
    public const string Git = "git";
    public const string Review = "review";
    public const string Build = "build";
    public const string Deploy = "deploy";

    /// <summary>Connection string configuration keys (ASP.NET Core ConnectionStrings section).</summary>
    public static class ConnectionKeys
    {
        public const string Platform = "Platform";
        public const string Git = "Git";
        public const string Review = "Review";
        public const string Build = "Build";
        public const string Deploy = "Deploy";
    }
}

/// <summary>
/// Resolves the active deployment profile from configuration / environment.
/// Env: <c>FORGEDECK_DEPLOYMENT_PROFILE</c> (appliance | team-split | enterprise).
/// </summary>
public static class DeploymentProfileResolver
{
    public const string EnvironmentVariable = "FORGEDECK_DEPLOYMENT_PROFILE";
    public const string ConfigurationKey = "Deployment:Profile";

    public static string Resolve(string? configuredOrEnv)
    {
        if (string.IsNullOrWhiteSpace(configuredOrEnv))
        {
            return DeploymentProfiles.Appliance;
        }

        return configuredOrEnv.Trim().ToLowerInvariant() switch
        {
            DeploymentProfiles.TeamSplit => DeploymentProfiles.TeamSplit,
            DeploymentProfiles.Enterprise => DeploymentProfiles.Enterprise,
            _ => DeploymentProfiles.Appliance,
        };
    }
}
