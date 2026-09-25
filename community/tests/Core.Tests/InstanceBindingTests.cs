using System.Security.Cryptography;
using System.Text.Json;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Review;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Core.Tests;

public sealed class InstanceBindingTests
{
    [Fact]
    public void Unbound_licence_installs_successfully()
    {
        using var harness = CreateHarness();
        var payload = SignLicence(harness.Keys, instanceId: null);
        var status = harness.Licences.InstallCommercial(payload);
        Assert.Equal("Active", status.Status);
    }

    [Fact]
    public void Licence_bound_to_other_instance_is_rejected()
    {
        using var harness = CreateHarness();
        var other = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var payload = SignLicence(harness.Keys, other);
        var ex = Assert.Throws<ArgumentException>(() => harness.Licences.InstallCommercial(payload));
        Assert.Contains("different ForgeDeck instance", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Licence_bound_to_local_instance_installs()
    {
        using var harness = CreateHarness();
        harness.Store.EnsureInstanceId();
        var local = harness.Store.GetInstance().InstanceId;
        var payload = SignLicence(harness.Keys, local);
        var status = harness.Licences.InstallCommercial(payload);
        Assert.Equal("Active", status.Status);
    }

    private static string SignLicence(LicenceKeyPair keys, Guid? instanceId)
    {
        var document = new LicenceDocument
        {
            OrganisationId = KnownIds.OrganisationId,
            IssuedAt = DateTimeOffset.UtcNow,
            InstanceId = instanceId,
            Modules =
            {
                ["review"] = new LicenceModuleDocument
                {
                    Edition = "Team",
                    Capabilities = [KnownCapabilities.Review.MultiApproval]
                }
            }
        };
        document.Signature = LicenceCryptography.Sign(document, keys);
        return JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static Harness CreateHarness()
    {
        var db = Path.Combine(Path.GetTempPath(), $"fd-bind-{Guid.NewGuid():N}.db");
        var connections = new SqliteConnectionFactory($"Data Source={db}");
        var schema = new CoreSchemaInitializer(connections);
        var store = new SqliteTenancyStore(connections, schema);
        store.EnsureInstanceId();

        var keys = LicenceCryptography.CreateKeyPair();
        var entitlements = new ManagedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null, keys);
        var modules = new[] { new ReviewModule() };
        var capabilities = new CapabilityService(modules, entitlements);
        var host = new FakeHost();
        var options = Options.Create(new LicensingOptions());
        var entitlementService = new EntitlementService(entitlements, store);
        var licences = new LicenceService(store, entitlements, modules, capabilities, entitlementService, options, host, NullLogger<LicenceService>.Instance);
        return new Harness(db, store, entitlements, licences, keys);
    }

    private sealed class FakeHost : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "ForgeDeck";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class Harness(
        string dbPath,
        ITenancyStore store,
        ManagedLicenceEntitlementStore entitlements,
        LicenceService licences,
        LicenceKeyPair keys) : IDisposable
    {
        public ITenancyStore Store => store;
        public LicenceService Licences => licences;
        public LicenceKeyPair Keys => keys;

        public void Dispose()
        {
            entitlements.Dispose();

            try { File.Delete(dbPath); } catch { /* ignore */ }
        }
    }
}
