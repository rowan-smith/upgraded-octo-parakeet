using System.Collections.Concurrent;
using System.Text.Json;
using ForgeDeck.Contracts.Modules;
using Microsoft.Extensions.Logging;

namespace ForgeDeck.Core.Modules;

public static class ModulePackageLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ModulePackageDocument ParseJson(string json) =>
        JsonSerializer.Deserialize<ModulePackageDocument>(json, JsonOptions)
        ?? throw new InvalidOperationException("Module package document was empty.");

    public static ModulePackageDocument LoadFile(string path)
    {
        var json = File.ReadAllText(path);
        // Prefer module.json; extension.json is mapped via FromLegacyExtensionJson when kind is missing.
        if (path.EndsWith("extension.json", StringComparison.OrdinalIgnoreCase))
        {
            return FromLegacyExtensionJson(json);
        }

        return ParseJson(json);
    }

    public static ModulePackageDocument FromLegacyExtensionJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var package = new ModulePackageDocument
        {
            Metadata =
            {
                Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Version = root.TryGetProperty("version", out var version) ? version.GetString() ?? "0.0.0" : "0.0.0",
                Publisher = root.TryGetProperty("publisher", out var publisher) ? publisher.GetString() : "ForgeDeck"
            },
            Edition = root.TryGetProperty("edition", out var edition) ? edition.GetString() : "community",
            Runtime = new ModulePackageRuntime { Type = "in-process" }
        };

        if (root.TryGetProperty("provides", out var provides) && provides.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in provides.EnumerateArray())
            {
                var value = item.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (value.Contains('.', StringComparison.Ordinal))
                {
                    package.Capabilities.Add(value);
                }
                else
                {
                    package.Provides.Add(value);
                }
            }
        }

        if (root.TryGetProperty("requires", out var requires) && requires.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in requires.EnumerateArray())
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    package.Requires.Add(value);
                }
            }
        }

        return package;
    }
}

/// <summary>Tracks in-process modules that implement <see cref="IModuleLifecycle"/>.</summary>
public sealed class InProcessModuleHost(ILogger<InProcessModuleHost> logger) : IModuleHost
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.OrdinalIgnoreCase);

    public ModuleRuntimeKind PreferredThirdPartyRuntime => ModuleRuntimeKind.InProcess;

    public Task StartAsync(ModulePackageDocument package, IModuleContext context, CancellationToken cancellationToken = default)
    {
        var id = package.Metadata.Id;
        _running[id] = 0;
        logger.LogInformation("In-process module host marked {ModuleId} as running (runtime={Runtime}).", id, package.Runtime?.Type);
        return Task.CompletedTask;
    }

    public Task StopAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        _running.TryRemove(moduleId, out _);
        return Task.CompletedTask;
    }

    public bool IsRunning(string moduleId) => _running.ContainsKey(moduleId);
}

/// <summary>
/// Scaffold for third-party isolation. Does not load remote processes yet — validates package
/// metadata and records intent so CI/architecture tests can assert the boundary exists.
/// </summary>
public sealed class OutOfProcessModuleHost(ILogger<OutOfProcessModuleHost> logger) : IModuleHost
{
    private readonly ConcurrentDictionary<string, ModulePackageDocument> _running = new(StringComparer.OrdinalIgnoreCase);

    public ModuleRuntimeKind PreferredThirdPartyRuntime => ModuleRuntimeKind.OutOfProcess;

    public Task StartAsync(ModulePackageDocument package, IModuleContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(package.Metadata.Id))
        {
            throw new ArgumentException("Module package metadata.id is required.");
        }

        var runtime = package.Runtime?.Type?.ToLowerInvariant() ?? "out-of-process";
        if (runtime is "in-process" or "inprocess")
        {
            throw new InvalidOperationException("OutOfProcessModuleHost refuses in-process packages; use InProcessModuleHost.");
        }

        // Future: spawn sidecar / attach gRPC channel / import container image.
        _running[package.Metadata.Id] = package;
        logger.LogInformation(
            "Out-of-process host accepted module {ModuleId} v{Version} (scaffold — no process spawned). Permissions={Permissions}",
            package.Metadata.Id,
            package.Metadata.Version,
            string.Join(',', package.Permissions));
        return Task.CompletedTask;
    }

    public Task StopAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        _running.TryRemove(moduleId, out _);
        return Task.CompletedTask;
    }

    public bool IsRunning(string moduleId) => _running.ContainsKey(moduleId);
}

/// <summary>Routes packages to in-process or out-of-process hosts based on runtime type.</summary>
public sealed class CompositeModuleHost(
    InProcessModuleHost inProcess,
    OutOfProcessModuleHost outOfProcess) : IModuleHost
{
    public ModuleRuntimeKind PreferredThirdPartyRuntime => ModuleRuntimeKind.OutOfProcess;

    public Task StartAsync(ModulePackageDocument package, IModuleContext context, CancellationToken cancellationToken = default)
    {
        var runtime = package.Runtime?.Type?.ToLowerInvariant() ?? "in-process";
        return runtime is "out-of-process" or "outofprocess" or "process" or "container"
            ? outOfProcess.StartAsync(package, context, cancellationToken)
            : inProcess.StartAsync(package, context, cancellationToken);
    }

    public Task StopAsync(string moduleId, CancellationToken cancellationToken = default) =>
        Task.WhenAll(inProcess.StopAsync(moduleId, cancellationToken), outOfProcess.StopAsync(moduleId, cancellationToken));

    public bool IsRunning(string moduleId) =>
        inProcess.IsRunning(moduleId) || outOfProcess.IsRunning(moduleId);
}
