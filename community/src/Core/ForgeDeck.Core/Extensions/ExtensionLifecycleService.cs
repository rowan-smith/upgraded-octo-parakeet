using ForgeDeck.Contracts.Audit;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Core.Context;
using ForgeDeck.Core.Identity;
using ForgeDeck.Messaging;
using Microsoft.Extensions.Configuration;

namespace ForgeDeck.Core.Extensions;

public sealed class ExtensionLifecycleService(
    IExtensionRegistry registry,
    IEnumerable<IPlatformModule> modules,
    ICapabilityService capabilities,
    PlatformContextStore context,
    IAuditWriter audit,
    IPermissionDefinitionRegistry? permissions = null,
    IModuleEventLifecycle? eventLifecycle = null,
    IServiceProvider? services = null,
    ExtensionPackageInstaller? packages = null)
{
    private static readonly HashSet<string> LoadedRuntimeIds = new(StringComparer.OrdinalIgnoreCase);

    public static void RememberLoaded(IEnumerable<IPlatformModule> loaded)
    {
        foreach (var module in loaded)
        {
            LoadedRuntimeIds.Add(module.Manifest.Id);
        }
    }

    public IReadOnlyList<ExtensionStatusView> List(ExtensionType? type = null)
    {
        var orgId = context.Organisation.Id;
        var installed = registry.List().ToDictionary(x => x.ExtensionId, StringComparer.OrdinalIgnoreCase);
        var packageRuntimeIds = modules.Select(m => m.Manifest.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        packageRuntimeIds.Add("github");

        return BuiltinExtensionCatalogue.All
            .Where(entry => type is null || entry.Type == type)
            .Where(entry => !IsDeprecatedAlias(entry) || installed.ContainsKey(entry.ExtensionId))
            .Where(entry => !IsUnlistedTierClone(entry) || installed.ContainsKey(entry.ExtensionId))
            .Select(entry => ToStatus(entry, installed.GetValueOrDefault(entry.ExtensionId), packageRuntimeIds, orgId))
            .ToArray();
    }

    public ExtensionStatusView Get(string extensionId)
    {
        var entry = BuiltinExtensionCatalogue.Find(extensionId)
            ?? throw new KeyNotFoundException($"Unknown extension '{extensionId}'.");
        var packageRuntimeIds = modules.Select(m => m.Manifest.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        packageRuntimeIds.Add("github");
        return ToStatus(entry, registry.Find(extensionId), packageRuntimeIds, context.Organisation.Id);
    }

    public ExtensionStatusView Install(string extensionId, string actor, bool enable = true)
    {
        var entry = BuiltinExtensionCatalogue.Find(extensionId)
            ?? throw new KeyNotFoundException($"Unknown extension '{extensionId}'.");
        if (!entry.Bundled)
        {
            throw new InvalidOperationException($"{entry.Name} is not available as a bundled package. Upload or configure a package feed.");
        }

        return CompleteInstall(entry, actor, enable);
    }

    /// <summary>
    /// Installs a commercial extension after verifying the package envelope signature.
    /// Possession of a package does not grant entitlement — capability checks still apply at runtime.
    /// </summary>
    public ExtensionStatusView InstallCommercialPackage(
        string extensionId,
        ExtensionPackageEnvelope envelope,
        IExtensionPackageVerifier verifier,
        string actor,
        bool enable = true)
    {
        var entry = BuiltinExtensionCatalogue.Find(extensionId)
            ?? throw new KeyNotFoundException($"Unknown extension '{extensionId}'.");
        if (entry.Bundled)
        {
            throw new InvalidOperationException($"{entry.Name} is a bundled Community package; use Install instead.");
        }

        var verification = verifier.Verify(envelope);
        if (!verification.Success)
        {
            throw new InvalidOperationException(verification.Error ?? "Package signature verification failed.");
        }

        if (!string.Equals(envelope.PackageId, entry.ExtensionId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Package id does not match the requested extension.");
        }

        return CompleteInstall(entry, actor, enable);
    }

    /// <summary>
    /// Stages an offline .fdext/.zip package, optionally verifies checksum, promotes to installed,
    /// then registers the catalogue entry without enabling on failure.
    /// </summary>
    public ExtensionStatusView InstallFromPackageFile(
        Stream packageStream,
        string actor,
        string? extensionIdHint = null,
        string? versionHint = null,
        string? expectedSha256 = null,
        bool enable = true)
    {
        if (packages is null)
        {
            throw new InvalidOperationException("Extension package installer is not configured.");
        }

        string? resolvedId = extensionIdHint;
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"forgedeck-upload-{Guid.NewGuid():N}.zip");
            try
            {
                using (var file = File.Create(tempPath))
                {
                    packageStream.CopyTo(file);
                }

                ExtensionPackageInstaller.VerifyChecksum(tempPath, expectedSha256);
                var digest = ExtensionPackageInstaller.ComputeSha256(tempPath);
                var staged = packages.StagePackage(tempPath, extensionIdHint, versionHint);
                resolvedId = staged.ExtensionId;
                packages.ValidatePreflight(staged);
                var promoted = packages.PromoteStaged(staged);
                return CompleteInstallFromPackage(promoted.ExtensionId, promoted.Version, actor, enable, digest);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            RecordInstallationFailed(resolvedId, actor, ex.Message);
            throw;
        }
    }

    public ExtensionStatusView InstallFromPackageFile(
        string packagePath,
        string actor,
        string? extensionIdHint = null,
        string? versionHint = null,
        string? expectedSha256 = null,
        bool enable = true)
    {
        using var stream = File.OpenRead(packagePath);
        return InstallFromPackageFile(stream, actor, extensionIdHint, versionHint, expectedSha256, enable);
    }

    private ExtensionStatusView CompleteInstallFromPackage(
        string extensionId,
        string version,
        string actor,
        bool enable,
        string? packageDigest = null)
    {
        var entry = BuiltinExtensionCatalogue.Find(extensionId);
        if (entry is null)
        {
            // Unknown catalogue id: record as installed connector/module shell so status APIs work after restart.
            entry = new ExtensionCatalogueEntry(
                extensionId,
                extensionId,
                ExtensionType.Module,
                version,
                "Uploaded",
                "Uploaded extension package.",
                [],
                RuntimeId: null,
                Provides: [],
                Requires: [],
                Optional: [],
                Bundled: false);
        }

        var existing = registry.Find(entry.ExtensionId);
        if (existing is { Enabled: true, State: ExtensionLifecycleState.Enabled })
        {
            return GetOrSynthesize(entry);
        }

        var now = DateTimeOffset.UtcNow;
        var installation = existing ?? new ExtensionInstallation
        {
            ExtensionId = entry.ExtensionId,
            Type = entry.Type,
            RuntimeId = entry.RuntimeId
        };
        installation.Type = entry.Type;
        installation.RuntimeId = entry.RuntimeId;
        installation.InstalledVersion = version;
        installation.InstalledAt ??= now;
        installation.InstalledBy = actor;
        installation.UpdatedAt = now;
        installation.LastError = null;
        installation.State = ExtensionLifecycleState.Installed;
        installation.Enabled = false;
        installation.RestartRequired = NeedsRestart(entry) || !entry.Bundled;
        installation.InstalledFrom = ExtensionInstalledFrom.Upload;
        installation.PackageDigest = string.IsNullOrWhiteSpace(packageDigest) ? installation.PackageDigest : packageDigest;
        registry.Save(installation);
        audit.Write("core", "module.installed", entry.ExtensionId, new { version, entry.Type, source = "package", packageDigest });

        if (enable && BuiltinExtensionCatalogue.Find(entry.ExtensionId) is not null)
        {
            return Enable(entry.ExtensionId, actor);
        }

        return GetOrSynthesize(entry);
    }

    private void RecordInstallationFailed(string? extensionId, string actor, string error)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        var entry = BuiltinExtensionCatalogue.Find(extensionId);
        var existing = registry.Find(extensionId);
        var installation = existing ?? new ExtensionInstallation
        {
            ExtensionId = extensionId,
            Type = entry?.Type ?? ExtensionType.Module,
            RuntimeId = entry?.RuntimeId
        };
        installation.Enabled = false;
        installation.State = ExtensionLifecycleState.Failed;
        installation.LastError = error;
        installation.UpdatedAt = DateTimeOffset.UtcNow;
        installation.InstalledBy ??= actor;
        registry.Save(installation);
        audit.Write("core", "module.install_failed", extensionId, new { actor, error });
    }

    private ExtensionStatusView GetOrSynthesize(ExtensionCatalogueEntry entry)
    {
        try
        {
            return Get(entry.ExtensionId);
        }
        catch (KeyNotFoundException)
        {
            var installation = registry.Find(entry.ExtensionId);
            return ToStatus(entry, installation, [], context.Organisation.Id);
        }
    }

    private ExtensionStatusView CompleteInstall(ExtensionCatalogueEntry entry, string actor, bool enable)
    {
        if (entry.ExtensionId.Contains("enterprise", StringComparison.OrdinalIgnoreCase) &&
            !capabilities.Has(context.Organisation.Id, KnownCapabilities.Review.CodeOwners) &&
            !capabilities.Has(context.Organisation.Id, KnownCapabilities.Review.MultiApproval))
        {
            // Soft gate: catalogue install may proceed; capabilities still enforce at runtime.
        }

        var existing = registry.Find(entry.ExtensionId);
        if (existing is { Enabled: true, State: ExtensionLifecycleState.Enabled })
        {
            return Get(entry.ExtensionId);
        }

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
        installation.InstalledFrom = entry.Bundled ? ExtensionInstalledFrom.Bundled : ExtensionInstalledFrom.Unknown;
        registry.Save(installation);
        audit.Write("core", "module.installed", entry.ExtensionId, new { entry.Version, entry.Type });

        if (enable)
        {
            return Enable(entry.ExtensionId, actor);
        }

        return Get(entry.ExtensionId);
    }

    public ExtensionStatusView Enable(string extensionId, string actor)
    {
        var entry = BuiltinExtensionCatalogue.Find(extensionId)
            ?? throw new KeyNotFoundException($"Unknown extension '{extensionId}'.");
        var installation = registry.Find(extensionId)
            ?? throw new InvalidOperationException($"{entry.Name} is not installed.");

        if (installation.State == ExtensionLifecycleState.Enabled && installation.Enabled)
        {
            return Get(extensionId);
        }

        installation.Enabled = true;
        installation.State = NeedsRestart(entry) && !IsRuntimeLoaded(entry)
            ? ExtensionLifecycleState.RestartRequired
            : ExtensionLifecycleState.Enabled;
        installation.RestartRequired = installation.State == ExtensionLifecycleState.RestartRequired;
        installation.UpdatedAt = DateTimeOffset.UtcNow;
        installation.LastError = null;
        registry.Save(installation);
        permissions?.SetExtensionActive(entry.ExtensionId, true);
        if (entry.RuntimeId is not null && services is not null)
        {
            eventLifecycle?.ReactivateModule(entry.RuntimeId, services);
        }

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
        permissions?.SetExtensionActive(entry.ExtensionId, false);
        if (entry.RuntimeId is not null)
        {
            eventLifecycle?.DeactivateModule(entry.RuntimeId);
        }

        audit.Write("core", "module.disabled", entry.ExtensionId, new { actor });
        return Get(extensionId);
    }

    public ExtensionStatusView Uninstall(string extensionId, string actor, bool preserveData = true)
    {
        var entry = BuiltinExtensionCatalogue.Find(extensionId)
            ?? throw new KeyNotFoundException($"Unknown extension '{extensionId}'.");
        var installation = registry.Find(extensionId);
        if (installation is null)
        {
            return Get(extensionId);
        }

        if (installation.Enabled)
        {
            throw new InvalidOperationException($"Disable {entry.Name} before uninstalling.");
        }

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

        foreach (var module in modules)
        {
            var extId = BuiltinExtensionCatalogue.ExtensionIdForModule(module.Manifest);
            if (extId is null || !registry.IsEnabled(extId))
            {
                continue;
            }

            foreach (var item in module.Manifest.Navigation)
            {
                nav.Add(new(extId, item.Id, item.Label, item.Route, item.Group, item.Order));
            }
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
            if (extId is null || !registry.IsEnabled(extId))
            {
                continue;
            }

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

        return list;
    }

    public void SeedDogfoodDefaults(string actor, IConfiguration? configuration = null)
    {
        var toInstall = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "forgedeck.code",
            "forgedeck.github"
        };
        foreach (var module in modules)
        {
            var extId = BuiltinExtensionCatalogue.ExtensionIdForModule(module.Manifest);
            if (extId is not null)
            {
                toInstall.Add(extId);
            }
        }

        foreach (var id in toInstall)
        {
            try
            {
                var entry = BuiltinExtensionCatalogue.Find(id);
                if (entry is not { Bundled: true })
                {
                    continue;
                }

                if (entry.RuntimeId is not null &&
                    configuration is not null &&
                    !configuration.GetValue($"Modules:{entry.RuntimeId}:Enabled", true))
                {
                    continue;
                }

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
        {
            state = ExtensionLifecycleState.Damaged;
        }

        var edition = CatalogueEdition(entry);
        var granted = Array.Empty<string>();
        if (entry.RuntimeId is not null)
        {
            var module = modules.FirstOrDefault(m => m.Manifest.Id.Equals(entry.RuntimeId, StringComparison.OrdinalIgnoreCase));
            if (module is not null)
            {
                edition = capabilities.EditionFor(orgId, module.Manifest.Id, module.Manifest.Edition);
                if (enabled)
                {
                    granted = capabilities.ForOrganisation(orgId)
                        .Where(c => BelongsToModule(c, module.Manifest))
                        .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
            }

            foreach (var commercial in modules.Where(m =>
                         IsProprietaryEdition(m.Manifest.Edition) &&
                         (m.Manifest.Id.StartsWith(entry.RuntimeId + "-", StringComparison.OrdinalIgnoreCase)
                          || m.Manifest.Id.Equals(entry.RuntimeId + "-commercial", StringComparison.OrdinalIgnoreCase))))
            {
                if (!enabled)
                {
                    break;
                }

                granted = granted.Concat(capabilities.ForOrganisation(orgId)
                        .Where(c => BelongsToModule(c, commercial.Manifest)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (!entry.RuntimeId.Equals("review", StringComparison.OrdinalIgnoreCase)
                    && !entry.RuntimeId.Equals("build", StringComparison.OrdinalIgnoreCase)
                    && !entry.RuntimeId.Equals("deploy", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entry.RuntimeId.Equals("review", StringComparison.OrdinalIgnoreCase))
                {
                    if (capabilities.Has(orgId, KnownCapabilities.Review.CodeOwners))
                    {
                        edition = "Enterprise";
                    }
                    else if (capabilities.Has(orgId, KnownCapabilities.Review.MultiApproval))
                    {
                        edition = "Team";
                    }
                }
                else if (entry.RuntimeId.Equals("build", StringComparison.OrdinalIgnoreCase))
                {
                    if (capabilities.Has(orgId, KnownCapabilities.Build.Attestation))
                    {
                        edition = "Enterprise";
                    }
                    else if (capabilities.Has(orgId, KnownCapabilities.Build.Concurrent))
                    {
                        edition = "Team";
                    }
                }
                else if (entry.RuntimeId.Equals("deploy", StringComparison.OrdinalIgnoreCase))
                {
                    if (capabilities.Has(orgId, KnownCapabilities.Deploy.MultiSite))
                    {
                        edition = "Enterprise";
                    }
                    else if (capabilities.Has(orgId, KnownCapabilities.Deploy.MultiEnvironment))
                    {
                        edition = "Team";
                    }
                }
            }
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
            EnterpriseFeaturesLicensed: capabilities.Has(orgId, KnownCapabilities.Review.MultiApproval),
            granted,
            installation?.InstalledFrom
                ?? (entry.Bundled && installed ? ExtensionInstalledFrom.Bundled : ExtensionInstalledFrom.Unknown),
            installation?.PackageDigest);
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

    private static bool IsDeprecatedAlias(ExtensionCatalogueEntry entry) =>
        entry.ExtensionId.EndsWith(".commercial", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Team/Enterprise/Commercial catalogue rows are package install targets, not separate Available cards.
    /// Hide them unless already installed; edition is licence-driven on the base module.
    /// </summary>
    public static bool IsUnlistedTierClone(ExtensionCatalogueEntry entry)
    {
        if (entry.Type != ExtensionType.Module)
        {
            return false;
        }

        var id = entry.ExtensionId;
        return id.EndsWith(".team", StringComparison.OrdinalIgnoreCase)
               || id.EndsWith(".enterprise", StringComparison.OrdinalIgnoreCase)
               || id.EndsWith(".commercial", StringComparison.OrdinalIgnoreCase);
    }

    private static string CatalogueEdition(ExtensionCatalogueEntry entry)
    {
        var id = entry.ExtensionId;
        if (id.EndsWith(".enterprise", StringComparison.OrdinalIgnoreCase))
        {
            return "Enterprise";
        }

        if (id.EndsWith(".team", StringComparison.OrdinalIgnoreCase)
            || id.EndsWith(".commercial", StringComparison.OrdinalIgnoreCase))
        {
            return "Team";
        }

        return "Community";
    }

    private static bool NeedsRestart(ExtensionCatalogueEntry entry) =>
        entry.RuntimeId is "git"; // rare packages not loaded by default

    private static bool IsRuntimeLoaded(ExtensionCatalogueEntry entry) =>
        entry.RuntimeId is null
        || entry.RuntimeId is "github"
        || LoadedRuntimeIds.Contains(entry.RuntimeId)
        || entry.RuntimeId is "code" or "review" or "build" or "deploy"; // always project-referenced in CE host

    private static bool IsProprietaryEdition(string edition) =>
        edition.Equals("Commercial", StringComparison.OrdinalIgnoreCase)
        || edition.Equals("Team", StringComparison.OrdinalIgnoreCase)
        || edition.Equals("Enterprise", StringComparison.OrdinalIgnoreCase);

    private static bool BelongsToModule(string capability, ModuleManifest manifest)
    {
        var dot = capability.IndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        var prefix = capability[..dot];
        var commercial = KnownCapabilities.IsCommercial(capability);
        var isProprietary = IsProprietaryEdition(manifest.Edition);
        if (commercial != isProprietary)
        {
            return false;
        }

        if (manifest.Id.Equals(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (isProprietary &&
            (manifest.Id.Equals(prefix + "-commercial", StringComparison.OrdinalIgnoreCase)
             || manifest.Id.Equals(prefix + "-team", StringComparison.OrdinalIgnoreCase)
             || manifest.Id.Equals(prefix + "-enterprise", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }
}
