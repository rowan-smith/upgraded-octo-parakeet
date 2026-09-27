using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ForgeDeck.Core.Extensions;

public sealed record ExtensionPackageStageResult(
    string ExtensionId,
    string Version,
    string StagingPath,
    string? ManifestPath,
    ModulePackageDocument? Manifest);

public sealed record ExtensionPackagePromoteResult(
    string ExtensionId,
    string Version,
    string InstalledPath);

/// <summary>
/// Stages, validates, and promotes offline extension packages (.fdext / .zip)
/// under {contentRoot}/data/extensions or Extensions:Root.
/// </summary>
public sealed class ExtensionPackageInstaller(
    IHostEnvironment environment,
    IConfiguration configuration,
    IExtensionRegistry registry)
{
    public string Root =>
        Path.GetFullPath(
            configuration["Extensions:Root"]
            ?? Path.Combine(environment.ContentRootPath, "data", "extensions"));

    public string StagingRoot => Path.Combine(Root, "staging");
    public string InstalledRoot => Path.Combine(Root, "installed");

    public ExtensionPackageStageResult StagePackage(Stream packageStream, string? extensionIdHint = null, string? versionHint = null)
    {
        EnsureWritableRoots();
        var tempArchive = Path.Combine(Path.GetTempPath(), $"forgedeck-pkg-{Guid.NewGuid():N}.zip");
        try
        {
            using (var file = File.Create(tempArchive))
            {
                packageStream.CopyTo(file);
            }

            return StagePackage(tempArchive, extensionIdHint, versionHint);
        }
        finally
        {
            TryDelete(tempArchive);
        }
    }

    public ExtensionPackageStageResult StagePackage(string packagePath, string? extensionIdHint = null, string? versionHint = null)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            throw new FileNotFoundException("Extension package file was not found.", packagePath);
        }

        EnsureWritableRoots();
        var extractTemp = Path.Combine(Path.GetTempPath(), $"forgedeck-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractTemp);
        try
        {
            ZipFile.ExtractToDirectory(packagePath, extractTemp, overwriteFiles: true);
            var manifestPath = FindManifest(extractTemp);
            ModulePackageDocument? manifest = null;
            if (manifestPath is not null)
            {
                manifest = ModulePackageLoader.LoadFile(manifestPath);
            }

            var extensionId = FirstNonEmpty(
                extensionIdHint,
                manifest?.Metadata.Id,
                TryReadLegacyId(extractTemp));
            var version = FirstNonEmpty(
                versionHint,
                manifest?.Metadata.Version,
                "0.0.0");

            if (string.IsNullOrWhiteSpace(extensionId))
            {
                throw new InvalidOperationException(
                    "Package must include extension.json/module.json with an id, or supply extensionId.");
            }

            var stagingPath = Path.Combine(StagingRoot, Sanitize(extensionId), Sanitize(version));
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
            CopyDirectory(extractTemp, stagingPath);

            var stagedManifest = FindManifest(stagingPath);
            return new ExtensionPackageStageResult(
                extensionId,
                version,
                stagingPath,
                stagedManifest,
                stagedManifest is null ? null : ModulePackageLoader.LoadFile(stagedManifest));
        }
        finally
        {
            TryDeleteDirectory(extractTemp);
        }
    }

    public void ValidatePreflight(ExtensionPackageStageResult staged)
    {
        EnsureWritableRoots();
        if (string.IsNullOrWhiteSpace(staged.ExtensionId))
        {
            throw new InvalidOperationException("Staged package is missing extension id.");
        }

        if (!Directory.Exists(staged.StagingPath))
        {
            throw new DirectoryNotFoundException($"Staging path '{staged.StagingPath}' was not found.");
        }

        var existing = registry.Find(staged.ExtensionId);
        if (existing is { Enabled: true, State: ExtensionLifecycleState.Enabled or ExtensionLifecycleState.RestartRequired })
        {
            throw new InvalidOperationException(
                $"Extension '{staged.ExtensionId}' is already enabled. Disable it before reinstalling.");
        }

        // Touch write probe under installed root.
        var probe = Path.Combine(InstalledRoot, ".write-probe");
        Directory.CreateDirectory(InstalledRoot);
        File.WriteAllText(probe, DateTimeOffset.UtcNow.ToString("O"));
        File.Delete(probe);

        if (staged.ManifestPath is null)
        {
            // Manifest is preferred but not strictly required when extensionId was supplied.
            return;
        }

        var manifest = staged.Manifest ?? ModulePackageLoader.LoadFile(staged.ManifestPath);
        if (!string.Equals(manifest.Metadata.Id, staged.ExtensionId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(manifest.Metadata.Id))
        {
            throw new InvalidOperationException(
                $"Manifest id '{manifest.Metadata.Id}' does not match staged extension id '{staged.ExtensionId}'.");
        }
    }

    public ExtensionPackagePromoteResult PromoteStaged(ExtensionPackageStageResult staged)
    {
        ValidatePreflight(staged);
        var installedPath = Path.Combine(InstalledRoot, Sanitize(staged.ExtensionId), Sanitize(staged.Version));
        var parent = Path.GetDirectoryName(installedPath)!;
        Directory.CreateDirectory(parent);

        var tempTarget = installedPath + $".promoting-{Guid.NewGuid():N}";
        try
        {
            CopyDirectory(staged.StagingPath, tempTarget);
            if (Directory.Exists(installedPath))
            {
                Directory.Delete(installedPath, recursive: true);
            }

            Directory.Move(tempTarget, installedPath);
        }
        catch
        {
            TryDeleteDirectory(tempTarget);
            throw;
        }

        TryDeleteDirectory(staged.StagingPath);
        return new ExtensionPackagePromoteResult(staged.ExtensionId, staged.Version, installedPath);
    }

    public static string ComputeSha256(Stream stream)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return ComputeSha256(stream);
    }

    public static void VerifyChecksum(string filePath, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return;
        }

        var actual = ComputeSha256(filePath);
        var expected = expectedSha256.Trim().Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Package checksum mismatch. Expected {expected}, got {actual}.");
        }
    }

    private void EnsureWritableRoots()
    {
        Directory.CreateDirectory(StagingRoot);
        Directory.CreateDirectory(InstalledRoot);
    }

    private static string? FindManifest(string root)
    {
        var module = Directory.EnumerateFiles(root, "module.json", SearchOption.AllDirectories).FirstOrDefault();
        if (module is not null)
        {
            return module;
        }

        return Directory.EnumerateFiles(root, "extension.json", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static string? TryReadLegacyId(string root)
    {
        var path = FindManifest(root);
        if (path is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("id", out var id))
            {
                return id.GetString();
            }

            if (doc.RootElement.TryGetProperty("metadata", out var metadata)
                && metadata.TryGetProperty("id", out var metaId))
            {
                return metaId.GetString();
            }
        }
        catch
        {
            // Ignore parse failures; caller will require an explicit id.
        }

        return null;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "_" : cleaned;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, destination));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
