namespace ForgeDeck.Contracts.Modules;

public sealed record ModuleManifest(
    string Id,
    string Name,
    string Version,
    string Edition,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<NavigationItem> Navigation,
    IReadOnlyList<ResourceTab> ResourceTabs,
    IReadOnlyList<string>? Permissions = null,
    IReadOnlyList<string>? Publishes = null,
    IReadOnlyList<string>? Subscribes = null,
    IReadOnlyList<string>? Provides = null,
    IReadOnlyList<string>? Requires = null,
    IReadOnlyList<string>? ExtensionPointContributions = null,
    ModuleRuntimeKind Runtime = ModuleRuntimeKind.InProcess);

public sealed record NavigationItem(string Id, string Label, string Route, string Group, int Order = 0);
public sealed record ResourceTab(string Resource, string Id, string Label, string DataEndpoint, int Order = 0);
