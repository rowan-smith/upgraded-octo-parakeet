using Platform.Contracts.Audit;
using Platform.Contracts.Capabilities;
using Platform.Contracts.Extensions;
using Platform.Contracts.Modules;
using Platform.Core.Context;
using Platform.Core.Identity;

namespace Platform.Core.Extensions;

public sealed class ExtensionLifecycleService(
    IExtensionRegistry registry,
    IEnumerable<IPlatformModule> modules,
    ICapabilityService capabilities,
    PlatformContextStore context,
    IAuditWriter audit)
{
    private static readonly HashSet<string> LoadedRuntimeIds = new(StringComparer.OrdinalIgnoreCase);

    public static void RememberLoaded(IEnumerable<IPlatformModule> loaded)
    {
        foreach (var module in loaded)
            LoadedRuntimeIds.Add(module.Manifest.Id);
    }

    public IReadOnlyList<ExtensionStatusView> List(ExtensionType? type = null)
    {
        var orgId = context.Organisation.Id;
        var installed = registry.List().ToDictionary(x => x.ExtensionId, StringComparer.OrdinalIgnoreCase);
        var packageRuntimeIds = modules.Select(m => m.Manifest.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Code is SPA-hosted; treat as always present when bundled.
        packageRuntimeIds.Add("code");
        packageRuntimeIds.Add("github");

        return BuiltinExtensionCatalogue.All
            .Where(entry => type is null || entry.Type == type)
            .Select(entry => ToStatus(entry, installed.GetValueOrDefault(entry.ExtensionId), packageRuntimeIds, orgId))
            .ToArray();
    }

    public ExtensionStatusView Get(string extensionId)
    {
        var entry = BuiltinExtensionCatalogue.Find(extensionId)
            ?? throw new KeyNotFoundException($"Unknown extension '{extensionId}'.");
        var packageRuntimeIds = modules.Select(m => m.Manifest.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        packageRuntimeIds.Add("code");
        packageRuntimeIds.Add("github");
        return ToStatus(entry, registry.Find(extensionId), packageRuntimeIds, context.Organisation.Id);
    }

    public ExtensionStatusView Install(string extensionId, string actor, bool enable = true)
    {
        var entry = BuiltinExtensionCatalogue.Find(extensionId)
            ?? throw new KeyNotFoundException($"Unknown extension '{extensionId}'.");
        if (!entry.Bundled)
            throw new InvalidOperationException($"{entry.Name} is not available as a bundled package. Upload or configure a package feed.");

        if (entry.EnterpriseOnly &&
            !capabilities.Has(context.Organisation.Id, KnownCapabilities.Review.MultiApproval) &&
            entry.ExtensionId.Contains("enterprise", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{entry.Name} requires an Enterprise licence.");

        var existing = registry.Find(extensionId);
        if (existing is { Enabled: true, State: ExtensionLifecycleState.Enabled })
            return Get(extensionId);

        var now = DateTimeOffset.UtcNow;
        var installation = existing ?? new ExtensionInstallation
        {
            ExtensionId = entry.ExtensionId,
            Type = entry.Type,
            RuntimeId = entry.RuntimeId
        };
        installation.Type = entry.Type;
        installation.RuntimeId = entry.RuntimeId;
        installation.InstalledVersion = entry.Version;
        installation.InstalledAt ??= now;
        installation.InstalledBy = actor;
        installation.UpdatedAt = now;
        installation.LastError = null;
        installation.State = ExtensionLifecycleState.Installed;
        installation.Enabled = false;
        installation.RestartRequired = NeedsRestart(entry);
        registry.Save(installation);
        audit.Write("core", "module.installed", entry.ExtensionId, new { entry.Version, entry.Type });

        if (enable) return Enable(extensionId, actor);
        return Get(extensionId);
    }

    public ExtensionStatusView Enable(string extensionId, string actor)
    {
        var entry = BuiltinExtensionCatalogue.Find(extensionId)
            ?? throw new KeyNotFoundException($"Unknown extension '{extensionId}'.");
        var installation = registry.Find(extensionId)
            ?? throw new InvalidOperationException($"{entry.Name} is not installed.");

        if (installation.State == ExtensionLifecycleState.Enabled && installation.Enabled)
            return Get(extensionId);

        installation.Enabled = true;
        installation.State = NeedsRestart(entry) && !IsRuntimeLoaded(entry)
            ? ExtensionLifecycleState.RestartRequired
            : ExtensionLifecycleState.Enabled;
        installation.RestartRequired = installation.State == ExtensionLifecycleState.RestartRequired;
        installation.UpdatedAt = DateTimeOffset.UtcNow;
        installation.LastError = null;
        registry.Save(installation);
        audit.Write("core", "module.enabled", entry.ExtensionId, new { actor, installation.State });
        return Get(extensionId);
    }

    public ExtensionStatusView Disable(string extensionId, string actor)
    {
        var entry = BuiltinExtensionCatalogue.Find(extensionId)
            ?? throw new KeyNotFoundException($"Unknown extension '{extensionId}'.");
        var installation = registry.Find(extensionId)
            ?? throw new InvalidOperationException($"{entry.Name} is not installed.");

        if (entry.Type == ExtensionType.Connector && entry.RuntimeId == "github")
        {
            // Soft block messaging is returned via lastError warning path; allow disable for now.
        }

        installation.Enabled = false;
        installation.State = ExtensionLifecycleState.Disabled;
        installation.RestartRequired = false;
        installation.UpdatedAt = DateTimeOffset.UtcNow;
        registry.Save(installation);
        audit.Write("core", "module.disabled", entry.ExtensionId, new { actor });
        return Get(extensionId);
    }

    public ExtensionStatusView Uninstall(string extensionId, string actor, bool preserveData = true)
    {
        var entry = BuiltinExtensionCatalogue.Find(extensionId)
            ?? throw new KeyNotFoundException($"Unknown extension '{extensionId}'.");
        var installation = registry.Find(extensionId);
        if (installation is null) return Get(extensionId);
        if (installation.Enabled)
            throw new InvalidOperationException($"Disable {entry.Name} before uninstalling.");

        registry.Delete(extensionId);
        audit.Write("core", "module.uninstalled", entry.ExtensionId, new { actor, preserveData });
        return Get(extensionId);
    }

    public bool IsRuntimeActive(string runtimeId) => registry.IsRuntimeEnabled(runtimeId);

    public ExtensionCompositionSnapshot Composition()
    {
        var enabled = List().Where(x => x.Enabled && x.State is ExtensionLifecycleState.Enabled or ExtensionLifecycleState.RestartRequired).ToArray();
        var enabledRuntime = enabled.Select(x => x.RuntimeId).Where(x => x is not null).Cast<string>().ToArray();
        var nav = new List<NavigationContribution>();

        if (enabledRuntime.Contains("code", StringComparer.OrdinalIgnoreCase))
        {
            nav.Add(new("forgedeck.code", "files", "Files", "/files", "Code", 10));
            nav.Add(new("forgedeck.code", "branches", "Branches", "/source-branches", "Code", 20));
            nav.Add(new("forgedeck.code", "commits", "Commits", "/commits", "Code", 30));
            nav.Add(new("forgedeck.code", "tags", "Tags", "/tags", "Code", 40));
        }

        foreach (var module in modules)
        {
            var extId = BuiltinExtensionCatalogue.ExtensionIdForModule(module.Manifest);
            if (extId is null || !registry.IsEnabled(extId)) continue;
            foreach (var item in module.Manifest.Navigation)
                nav.Add(new(extId, item.Id, item.Label, item.Route, item.Group, item.Order));
        }

        var moduleViews = enabled.Where(x => x.Type == ExtensionType.Module).Select(ToCompositionModule).Cast<object>().ToArray();
        var connectorViews = enabled.Where(x => x.Type == ExtensionType.Connector).Select(ToCompositionModule).Cast<object>().ToArray();
        return new ExtensionCompositionSnapshot(moduleViews, connectorViews, nav, enabledRuntime, enabled.Select(x => x.ExtensionId).ToArray());
    }

    /// <summary>Modules payload compatible with existing SPA hasModule(id) using runtime ids.</summary>
    public IReadOnlyList<object> ActiveModulesForSpa()
    {
        var orgId = context.Organisation.Id;
        var list = new List<object>();
        foreach (var module in modules)
        {
            var extId = BuiltinExtensionCatalogue.ExtensionIdForModule(module.Manifest);
            if (extId is null || !registry.IsEnabled(extId)) continue;
            var granted = capabilities.ForOrganisation(orgId)
                .Where(c => BelongsToModule(c, module.Manifest))
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            list.Add(new
            {
                module.Manifest.Id,
                module.Manifest.Name,
                module.Manifest.Version,
                edition = capabilities.EditionFor(orgId, module.Manifest.Id, module.Manifest.Edition),
                capabilities = granted,
                module.Manifest.Navigation,
                module.Manifest.ResourceTabs,
                enabled = true,
                extensionId = extId
            });
        }

        if (registry.IsEnabled("forgedeck.code"))
        {
            list.Add(new
            {
                id = "code",
                name = "Code",
                version = "1.0.0",
                edition = "Community",
                capabilities = Array.Empty<string>(),
                navigation = new[]
                {
                    new { id = "files", label = "Files", route = "/files", group = "Code", order = 10 },
                    new { id = "branches", label = "Branches", route = "/source-branches", group = "Code", order = 20 },
                    new { id = "commits", label = "Commits", route = "/commits", group = "Code", order = 30 },
                    new { id = "tags", label = "Tags", route = "/tags", group = "Code", order = 40 }
                },
                resourceTabs = Array.Empty<object>(),
                enabled = true,
                extensionId = "forgedeck.code"
            });
        }

        return list;
    }

    public void SeedDogfoodDefaults(string actor)
    {
        foreach (var id in new[] { "forgedeck.code", "forgedeck.review", "forgedeck.build", "forgedeck.github" })
        {
            try
            {
                if (BuiltinExtensionCatalogue.Find(id)?.Bundled == true)
                    Install(id, actor, enable: true);
            }
            catch
            {
                // Catalogue/package gaps should not block seed.
            }
        }
    }

    private ExtensionStatusView ToStatus(
        ExtensionCatalogueEntry entry,
        ExtensionInstallation? installation,
        HashSet<string> packageRuntimeIds,
        Guid orgId)
    {
        var packagePresent = entry.RuntimeId is null
            || packageRuntimeIds.Contains(entry.RuntimeId)
            || entry.Bundled && entry.RuntimeId is "code" or "github";
        var installed = installation is not null &&
            installation.State is not ExtensionLifecycleState.Available;
        var enabled = installation is { Enabled: true } &&
            installation.State is ExtensionLifecycleState.Enabled or ExtensionLifecycleState.RestartRequired;
        var state = installation?.State ?? ExtensionLifecycleState.Available;
        if (!packagePresent && installed)
            state = ExtensionLifecycleState.Damaged;

        var edition = "Community";
        if (entry.RuntimeId is not null)
        {
            var module = modules.FirstOrDefault(m => m.Manifest.Id.Equals(entry.RuntimeId, StringComparison.OrdinalIgnoreCase));
            if (module is not null)
                edition = capabilities.EditionFor(orgId, module.Manifest.Id, module.Manifest.Edition);
        }

        return new ExtensionStatusView(
            entry.ExtensionId,
            entry.Name,
            entry.Type,
            installation?.InstalledVersion ?? entry.Version,
            entry.Publisher,
            entry.Summary,
            entry.Highlights,
            entry.RuntimeId,
            state,
            installed,
            enabled,
            entry.Bundled,
            packagePresent,
            installation?.RestartRequired ?? false,
            state == ExtensionLifecycleState.Failed || state == ExtensionLifecycleState.Damaged
                ? ExtensionHealth.Failed
                : enabled ? ExtensionHealth.Healthy : ExtensionHealth.Unknown,
            installation?.LastError,
            entry.Provides,
            entry.Requires,
            entry.Optional,
            edition,
            CommunityFeaturesAvailable: true,
            EnterpriseFeaturesLicensed: capabilities.Has(orgId, KnownCapabilities.Review.MultiApproval));
    }

    private static object ToCompositionModule(ExtensionStatusView status) => new
    {
        id = status.ExtensionId,
        runtimeId = status.RuntimeId,
        name = status.Name,
        version = status.Version,
        state = status.State.ToString(),
        enabled = status.Enabled
    };

    private static bool NeedsRestart(ExtensionCatalogueEntry entry) =>
        entry.RuntimeId is "git" or "deploy"; // rare packages not loaded by default

    private static bool IsRuntimeLoaded(ExtensionCatalogueEntry entry) =>
        entry.RuntimeId is null
        || entry.RuntimeId is "code" or "github"
        || LoadedRuntimeIds.Contains(entry.RuntimeId)
        || entry.RuntimeId is "review" or "pipelines"; // always project-referenced in CE host

    private static bool BelongsToModule(string capability, ModuleManifest manifest)
    {
        var dot = capability.IndexOf('.');
        if (dot <= 0) return false;
        var prefix = capability[..dot];
        var commercial = KnownCapabilities.IsCommercial(capability);
        var isCommercialModule = manifest.Edition.Equals("Commercial", StringComparison.OrdinalIgnoreCase);
        if (commercial != isCommercialModule) return false;
        if (manifest.Id.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        if (isCommercialModule && manifest.Id.Equals(prefix + "-commercial", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
