namespace Platform.Contracts.Extensions;

public enum ExtensionType
{
    Module,
    Connector
}

public enum ExtensionLifecycleState
{
    Available,
    Installing,
    Installed,
    Enabling,
    Enabled,
    Disabling,
    Disabled,
    Updating,
    Uninstalling,
    Failed,
    RestartRequired,
    Damaged
}

public enum ExtensionHealth
{
    Healthy,
    Degraded,
    Failed,
    Unknown
}

/// <summary>Catalogue metadata for a Module or Connector package.</summary>
public sealed record ExtensionCatalogueEntry(
    string ExtensionId,
    string Name,
    ExtensionType Type,
    string Version,
    string Publisher,
    string Summary,
    IReadOnlyList<string> Highlights,
    string? RuntimeId,
    IReadOnlyList<string> Provides,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Optional,
    bool Bundled,
    bool EnterpriseOnly = false);

/// <summary>Durable installation row for the Extension Registry.</summary>
public sealed class ExtensionInstallation
{
    public required string ExtensionId { get; set; }
    public ExtensionType Type { get; set; }
    public string? RuntimeId { get; set; }
    public string? InstalledVersion { get; set; }
    public ExtensionLifecycleState State { get; set; } = ExtensionLifecycleState.Available;
    public bool Enabled { get; set; }
    public DateTimeOffset? InstalledAt { get; set; }
    public string? InstalledBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastError { get; set; }
    public bool RestartRequired { get; set; }
}

public sealed record ExtensionStatusView(
    string ExtensionId,
    string Name,
    ExtensionType Type,
    string Version,
    string Publisher,
    string Summary,
    IReadOnlyList<string> Highlights,
    string? RuntimeId,
    ExtensionLifecycleState State,
    bool Installed,
    bool Enabled,
    bool Bundled,
    bool PackagePresent,
    bool RestartRequired,
    ExtensionHealth Health,
    string? LastError,
    IReadOnlyList<string> Provides,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Optional,
    string Edition,
    bool CommunityFeaturesAvailable,
    bool EnterpriseFeaturesLicensed);

public sealed record ExtensionCompositionSnapshot(
    IReadOnlyList<object> Modules,
    IReadOnlyList<object> Connectors,
    IReadOnlyList<NavigationContribution> Navigation,
    IReadOnlyList<string> EnabledRuntimeIds,
    IReadOnlyList<string> EnabledExtensionIds);

public sealed record NavigationContribution(
    string ExtensionId,
    string Id,
    string Label,
    string Route,
    string Group,
    int Order);
