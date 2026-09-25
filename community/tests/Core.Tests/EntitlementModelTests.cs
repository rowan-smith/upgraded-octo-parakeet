using System.Security.Cryptography;
using System.Text.Json;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Review;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Core.Tests;

public sealed class EntitlementModelTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void No_licence_means_community_for_all_modules()
    {
        var store = CreateSqliteStore();
        var entitlements = new EntitlementService(new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null), store);
        Assert.Equal(EntitlementLevel.Community, entitlements.GetEntitlement(OrganisationId, PlatformModules.Review));
        Assert.Equal(EntitlementLevel.Community, entitlements.GetEntitlement(OrganisationId, PlatformModules.Build));
    }

    [Fact]
    public void Tier_only_licence_expands_capabilities()
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = LicenceSkuCatalog.CreateDocument(LicenceSkuCatalog.TeamBundle, OrganisationId);
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var parsed = ManagedLicenceEntitlementStore.ParseVerified(json, keys)!;

        Assert.Contains(KnownCapabilities.Review.MultiApproval, parsed.Modules[PlatformModules.Review].Capabilities);
        Assert.Contains(KnownCapabilities.Review.CodeOwners, parsed.Modules[PlatformModules.Review].Capabilities);
        Assert.DoesNotContain(KnownCapabilities.Review.SeparationOfDuties, parsed.Modules[PlatformModules.Review].Capabilities);
        Assert.Equal(EntitlementLevel.Team, parsed.Modules[PlatformModules.Build].Level);
    }

    [Fact]
    public void Mixed_module_entitlements_are_independent()
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = new LicenceDocument
        {
            OrganisationId = OrganisationId,
            IssuedAt = DateTimeOffset.UtcNow,
            Modules =
            {
                [PlatformModules.Review] = new LicenceModuleDocument { Edition = "enterprise" },
                [PlatformModules.Build] = new LicenceModuleDocument { Edition = "team" },
                [PlatformModules.Deploy] = new LicenceModuleDocument { Edition = "team" },
                [PlatformModules.Git] = new LicenceModuleDocument { Edition = "community" },
                [PlatformModules.Code] = new LicenceModuleDocument { Edition = "community" }
            }
        };
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var entitlement = ManagedLicenceEntitlementStore.ParseVerified(json, keys)!;
        var store = CreateSqliteStore();
        var service = new EntitlementService(new SignedLicenceEntitlementStore(entitlement), store);

        Assert.Equal(EntitlementLevel.Enterprise, service.GetEntitlement(OrganisationId, PlatformModules.Review));
        Assert.Equal(EntitlementLevel.Team, service.GetEntitlement(OrganisationId, PlatformModules.Build));
        Assert.Equal(EntitlementLevel.Community, service.GetEntitlement(OrganisationId, PlatformModules.Git));
    }

    [Fact]
    public void Soft_seats_warn_but_do_not_block()
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = LicenceSkuCatalog.CreateDocument("REVIEW-TEAM", OrganisationId, maxUsers: 1);
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var entitlement = ManagedLicenceEntitlementStore.ParseVerified(json, keys)!;
        var store = CreateSqliteStore(userCount: 3);
        var service = new EntitlementService(new SignedLicenceEntitlementStore(entitlement), store);
        var seats = service.GetSeatStatus(OrganisationId);
        Assert.True(seats.OverCapacity);
        Assert.Contains("over its licensed capacity", seats.Warning);
    }

    [Fact]
    public void Maintenance_expiry_does_not_strip_module_entitlements()
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = LicenceSkuCatalog.CreateDocument(
            LicenceSkuCatalog.EnterpriseBundle,
            OrganisationId,
            maintenanceUntil: DateTimeOffset.UtcNow.AddDays(-1),
            maxMajor: 5);
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var entitlement = ManagedLicenceEntitlementStore.ParseVerified(json, keys)!;
        var store = CreateSqliteStore();
        var service = new EntitlementService(new SignedLicenceEntitlementStore(entitlement), store);

        Assert.Equal(EntitlementLevel.Enterprise, service.GetEntitlement(OrganisationId, PlatformModules.Review));
        var maintenance = service.GetMaintenanceStatus();
        Assert.True(maintenance.MaintenanceExpired);
        Assert.True(maintenance.VersionAllowed);
    }

    [Fact]
    public void Licence_request_includes_stable_instance_id()
    {
        using var harness = CreateHarness();
        var request = harness.Licences.CreateLicenceRequest();
        Assert.NotEqual(Guid.Empty, request.InstanceId);
        Assert.Equal(request.InstanceId, harness.Licences.CreateLicenceRequest().InstanceId);
    }

    [Fact]
    public void Sku_catalog_expands_a_la_carte_build_team()
    {
        var map = LicenceSkuCatalog.Expand("BUILD-TEAM");
        Assert.Equal(EntitlementLevel.Team, map[PlatformModules.Build]);
        Assert.Equal(EntitlementLevel.Community, map[PlatformModules.Review]);
    }

    [Fact]
    public void Community_capability_service_unaffected_without_commercial_packages()
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = LicenceSkuCatalog.CreateDocument("REVIEW-TEAM", OrganisationId);
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var entitlement = ManagedLicenceEntitlementStore.ParseVerified(json, keys)!;
        var caps = new CapabilityService([new ReviewModule()], new SignedLicenceEntitlementStore(entitlement));
        Assert.False(caps.Has(OrganisationId, KnownCapabilities.Review.MultiApproval));
        Assert.True(caps.Has(OrganisationId, KnownCapabilities.Review.BasicApproval));
    }

    private static SqliteTenancyStore CreateSqliteStore(int userCount = 0)
    {
        var db = Path.Combine(Path.GetTempPath(), $"fd-ent-store-{Guid.NewGuid():N}.db");
        var connections = new SqliteConnectionFactory($"Data Source={db}");
        var schema = new CoreSchemaInitializer(connections);
        var store = new SqliteTenancyStore(connections, schema);
        store.EnsureInstanceId();
        for (var i = 0; i < userCount; i++)
        {
            store.SaveUser(new ForgeDeck.Core.Domain.UserAccount
            {
                Id = Guid.NewGuid(),
                Email = $"u{i}@example.test",
                Username = $"user{i}",
                PasswordHash = "x"
            });
        }

        return store;
    }

    private static Harness CreateHarness()
    {
        var db = Path.Combine(Path.GetTempPath(), $"fd-ent-{Guid.NewGuid():N}.db");
        var connections = new SqliteConnectionFactory($"Data Source={db}");
        var schema = new CoreSchemaInitializer(connections);
        var store = new SqliteTenancyStore(connections, schema);
        store.EnsureInstanceId();
        var keys = LicenceCryptography.CreateKeyPair();
        var entitlements = new ManagedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null, keys);
        var modules = new[] { new ReviewModule() };
        var capabilities = new CapabilityService(modules, entitlements);
        var host = new FakeHost();
        var entitlementService = new EntitlementService(entitlements, store);
        var licences = new LicenceService(
            store, entitlements, modules, capabilities, entitlementService,
            Options.Create(new LicensingOptions()), host, NullLogger<LicenceService>.Instance);
        return new Harness(db, licences, keys, entitlements);
    }

    private sealed class FakeHost : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "ForgeDeck";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class Harness(string dbPath, LicenceService licences, LicenceKeyPair keys, ManagedLicenceEntitlementStore entitlements) : IDisposable
    {
        public LicenceService Licences => licences;
        public void Dispose()
        {
            entitlements.Dispose();
            _ = keys;
            try { File.Delete(dbPath); } catch { /* ignore */ }
        }
    }
}
