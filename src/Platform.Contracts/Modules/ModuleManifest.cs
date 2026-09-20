namespace Platform.Contracts.Modules;

public sealed record ModuleManifest(
    string Id,
    string Name,
    string Version,
    string Edition,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<NavigationItem> Navigation,
    IReadOnlyList<ResourceTab> ResourceTabs);

public sealed record NavigationItem(string Id, string Label, string Route, string Group, int Order = 0);
public sealed record ResourceTab(string Resource, string Id, string Label, string DataEndpoint, int Order = 0);
