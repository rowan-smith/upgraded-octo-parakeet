using System.Text.Json.Serialization;

namespace ForgeDeck.Contracts.Modules;

/// <summary>
/// Declarative package document (module.json / conceptual module.yaml).
/// First-party and third-party modules share this shape.
/// </summary>
public sealed class ModulePackageDocument
{
    public string ApiVersion { get; set; } = "platform.dev/v1";
    public string Kind { get; set; } = "Module";
    public ModulePackageMetadata Metadata { get; set; } = new();
    public ModulePackageCompatibility? Compatibility { get; set; }
    public ModulePackageRuntime? Runtime { get; set; }
    public ModulePackageUi? Ui { get; set; }
    public List<string> Permissions { get; set; } = [];
    public ModulePackageEvents? Events { get; set; }
    public ModulePackageLicensing? Licensing { get; set; }
    public List<string> Provides { get; set; } = [];
    public List<string> Requires { get; set; } = [];
    public List<string> Capabilities { get; set; } = [];
    public List<string> ExtensionPoints { get; set; } = [];
    public string? Edition { get; set; }
}

public sealed class ModulePackageMetadata
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "0.0.0";
    public string? Publisher { get; set; }
}

public sealed class ModulePackageCompatibility
{
    public string? Platform { get; set; }
}

public sealed class ModulePackageRuntime
{
    public string Type { get; set; } = "in-process";
    public string? Image { get; set; }
}

public sealed class ModulePackageUi
{
    public string? Entrypoint { get; set; }
}

public sealed class ModulePackageEvents
{
    public List<string> Subscribes { get; set; } = [];
    public List<string> Publishes { get; set; } = [];
}

public sealed class ModulePackageLicensing
{
    public string Provider { get; set; } = "platform";
    public string? Feature { get; set; }
    public string? ModuleId { get; set; }
}

/// <summary>Maps a package document into the runtime <see cref="ModuleManifest"/> shape.</summary>
public static class ModulePackageMapper
{
    public static ModuleManifest ToManifest(ModulePackageDocument document, IReadOnlyList<NavigationItem>? navigation = null, IReadOnlyList<ResourceTab>? tabs = null)
    {
        var runtime = document.Runtime?.Type?.ToLowerInvariant() switch
        {
            "out-of-process" or "outofprocess" or "process" => ModuleRuntimeKind.OutOfProcess,
            "container" => ModuleRuntimeKind.Container,
            _ => ModuleRuntimeKind.InProcess
        };

        return new ModuleManifest(
            document.Metadata.Id,
            document.Metadata.Name,
            document.Metadata.Version,
            string.IsNullOrWhiteSpace(document.Edition) ? "Community" : document.Edition,
            document.Capabilities,
            navigation ?? [],
            tabs ?? [],
            document.Permissions,
            document.Events?.Publishes,
            document.Events?.Subscribes,
            document.Provides,
            document.Requires,
            document.ExtensionPoints,
            runtime);
    }
}
