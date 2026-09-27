using System.IO.Compression;
using System.Text;
using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Core.Tests;

public sealed class ExtensionPackageInstallTests
{
    [Fact]
    public void Valid_package_stages_and_promotes_atomically()
    {
        using var host = new TestHost();
        var registry = new MemoryExtensionRegistry();
        var installer = new ExtensionPackageInstaller(host, host.Configuration, registry);

        var package = CreateZipPackage("""
            {
              "apiVersion": "platform.dev/v1",
              "kind": "Module",
              "metadata": { "id": "forgedeck.example", "name": "Example", "version": "1.2.3" },
              "capabilities": []
            }
            """);

        try
        {
            using var stream = File.OpenRead(package);
            var staged = installer.StagePackage(stream, "forgedeck.example", "1.2.3");
            Assert.True(Directory.Exists(staged.StagingPath));
            Assert.Equal("forgedeck.example", staged.ExtensionId);

            installer.ValidatePreflight(staged);
            var promoted = installer.PromoteStaged(staged);
            Assert.True(Directory.Exists(promoted.InstalledPath));
            Assert.False(Directory.Exists(staged.StagingPath));
        }
        finally
        {
            TryDelete(package);
            TryDeleteDirectory(installer.Root);
        }
    }

    [Fact]
    public void Preflight_rejects_enabled_duplicate_without_promoting()
    {
        using var host = new TestHost();
        var registry = new MemoryExtensionRegistry();
        registry.Save(new ExtensionInstallation
        {
            ExtensionId = "forgedeck.example",
            Type = ExtensionType.Module,
            InstalledVersion = "1.0.0",
            State = ExtensionLifecycleState.Enabled,
            Enabled = true,
            RuntimeId = "example"
        });

        var installer = new ExtensionPackageInstaller(host, host.Configuration, registry);
        var package = CreateZipPackage("""
            {
              "apiVersion": "platform.dev/v1",
              "kind": "Module",
              "metadata": { "id": "forgedeck.example", "name": "Example", "version": "2.0.0" }
            }
            """);

        try
        {
            using var stream = File.OpenRead(package);
            var staged = installer.StagePackage(stream);
            var error = Assert.Throws<InvalidOperationException>(() => installer.ValidatePreflight(staged));
            Assert.Contains("already", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(installer.InstalledRoot, "forgedeck.example", "2.0.0")));
        }
        finally
        {
            TryDelete(package);
            TryDeleteDirectory(installer.Root);
        }
    }

    [Fact]
    public void Bad_checksum_fails_before_promotion()
    {
        using var host = new TestHost();
        var registry = new MemoryExtensionRegistry();
        var installer = new ExtensionPackageInstaller(host, host.Configuration, registry);
        var package = CreateZipPackage("""
            {
              "apiVersion": "platform.dev/v1",
              "kind": "Module",
              "metadata": { "id": "forgedeck.checksum", "name": "Checksum", "version": "1.0.0" }
            }
            """);

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ExtensionPackageInstaller.VerifyChecksum(package, new string('a', 64)));
            Assert.Contains("checksum", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(installer.InstalledRoot, "forgedeck.checksum")));
        }
        finally
        {
            TryDelete(package);
            TryDeleteDirectory(installer.Root);
        }
    }

    private static string CreateZipPackage(string moduleJson)
    {
        var path = Path.Combine(Path.GetTempPath(), $"forgedeck-pkg-{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("module.json");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(moduleJson);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* ignore */ }
    }

    private sealed class TestHost : IHostEnvironment, IDisposable
    {
        public TestHost()
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), $"forgedeck-host-{Guid.NewGuid():N}");
            Directory.CreateDirectory(ContentRootPath);
            Configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Extensions:Root"] = Path.Combine(ContentRootPath, "extensions")
                })
                .Build();
        }

        public IConfiguration Configuration { get; }
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ForgeDeck.Tests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider
        {
            get => new PhysicalFileProvider(ContentRootPath);
            set { }
        }

        public void Dispose() => TryDeleteDirectory(ContentRootPath);
    }

    private sealed class MemoryExtensionRegistry : IExtensionRegistry
    {
        private readonly Dictionary<string, ExtensionInstallation> _items = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ExtensionInstallation> List() => _items.Values.ToArray();

        public ExtensionInstallation? Find(string extensionId) =>
            _items.TryGetValue(extensionId, out var item) ? item : null;

        public bool IsEnabled(string extensionId) =>
            Find(extensionId) is { Enabled: true, State: ExtensionLifecycleState.Enabled or ExtensionLifecycleState.RestartRequired };

        public bool IsRuntimeEnabled(string runtimeId) =>
            _items.Values.Any(i =>
                i.Enabled && string.Equals(i.RuntimeId, runtimeId, StringComparison.OrdinalIgnoreCase));

        public void Save(ExtensionInstallation installation) => _items[installation.ExtensionId] = installation;

        public void Delete(string extensionId) => _items.Remove(extensionId);

        public bool HasAnyInstalled() => _items.Values.Any(i => i.State is not ExtensionLifecycleState.Available);
    }
}
