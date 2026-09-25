using ForgeDeck.Build;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Checks;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.SourceControl;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Context;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Core.Modules;
using ForgeDeck.Git;
using ForgeDeck.Review;
using Microsoft.Extensions.Logging.Abstractions;

namespace Core.Tests;

public sealed class ModuleContractTests
{
    [Fact]
    public void Module_package_document_round_trips_to_manifest()
    {
        var json = """
            {
              "apiVersion": "platform.dev/v1",
              "kind": "Module",
              "metadata": { "id": "acme.security", "name": "Acme", "version": "1.0.0" },
              "runtime": { "type": "out-of-process" },
              "permissions": [ "repository.read" ],
              "events": { "subscribes": [ "repository.push" ], "publishes": [ "security.scan.completed" ] },
              "provides": [ "security-scanning" ],
              "capabilities": [ "Security.Scan" ],
              "extensionPoints": [ "review.checks" ]
            }
            """;
        var package = ModulePackageLoader.ParseJson(json);
        var manifest = ModulePackageMapper.ToManifest(package);
        Assert.Equal("acme.security", manifest.Id);
        Assert.Equal(ModuleRuntimeKind.OutOfProcess, manifest.Runtime);
        Assert.Contains(PlatformPermissions.RepositoryRead, manifest.Permissions!);
        Assert.Contains(ExtensionPoints.ReviewChecks, manifest.ExtensionPointContributions!);
    }

    [Fact]
    public void Legacy_extension_json_maps_into_package_document()
    {
        var package = ModulePackageLoader.FromLegacyExtensionJson("""
            { "id": "forgedeck.review", "name": "Review", "version": "0.3.0", "edition": "community",
              "provides": [ "review", "Review.BasicApproval" ] }
            """);
        Assert.Equal("forgedeck.review", package.Metadata.Id);
        Assert.Contains("review", package.Provides);
        Assert.Contains("Review.BasicApproval", package.Capabilities);
    }

    [Fact]
    public void First_party_modules_declare_permissions_and_events()
    {
        Assert.NotEmpty(new ReviewModule().Manifest.Permissions!);
        Assert.NotEmpty(new ReviewModule().Manifest.Publishes!);
        Assert.Contains("build.execution", new BuildModule().Manifest.Provides!);
        Assert.Contains(PlatformPermissions.RepositoryRead, new GitModule().Manifest.Permissions!);
    }

    [Fact]
    public void Provider_catalogue_exposes_check_without_naming_build_module()
    {
        var catalogue = new ProviderCatalogue(
            [new BuildModule(), new ReviewModule()],
            Array.Empty<ISourceProvider>(),
            Array.Empty<IChangeSourceProvider>(),
            [new FakeCheckProvider()]);
        Assert.True(catalogue.Has(ProviderKind.Check));
        Assert.Contains(catalogue.OfKind(ProviderKind.Check), r => r.ContractName == nameof(ICheckProvider));
    }

    [Fact]
    public async Task Out_of_process_host_accepts_third_party_package_scaffold()
    {
        var host = new OutOfProcessModuleHost(NullLogger<OutOfProcessModuleHost>.Instance);
        var package = ModulePackageLoader.ParseJson("""
            {
              "metadata": { "id": "acme.security", "name": "Acme", "version": "2.4.1" },
              "runtime": { "type": "out-of-process" },
              "permissions": [ "repository.read", "build.read" ]
            }
            """);
        await host.StartAsync(package, CreateContext());
        Assert.True(host.IsRunning("acme.security"));
        await host.StopAsync("acme.security");
        Assert.False(host.IsRunning("acme.security"));
    }

    [Fact]
    public async Task Out_of_process_host_rejects_in_process_packages()
    {
        var host = new OutOfProcessModuleHost(NullLogger<OutOfProcessModuleHost>.Instance);
        var package = ModulePackageLoader.ParseJson("""
            { "metadata": { "id": "forgedeck.review", "name": "Review", "version": "1.0.0" },
              "runtime": { "type": "in-process" } }
            """);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(package, CreateContext()));
    }

    [Fact]
    public async Task Composite_host_routes_by_runtime()
    {
        var composite = new CompositeModuleHost(
            new InProcessModuleHost(NullLogger<InProcessModuleHost>.Instance),
            new OutOfProcessModuleHost(NullLogger<OutOfProcessModuleHost>.Instance));
        var context = CreateContext();
        await composite.StartAsync(ModulePackageLoader.ParseJson("""
            { "metadata": { "id": "forgedeck.git", "name": "Git", "version": "1.0.0" }, "runtime": { "type": "in-process" } }
            """), context);
        await composite.StartAsync(ModulePackageLoader.ParseJson("""
            { "metadata": { "id": "acme.security", "name": "Acme", "version": "1.0.0" }, "runtime": { "type": "out-of-process" } }
            """), context);
        Assert.True(composite.IsRunning("forgedeck.git"));
        Assert.True(composite.IsRunning("acme.security"));
    }

    private static IModuleContext CreateContext()
    {
        var capabilities = new CapabilityService([], new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null));
        return new ModuleContext(
            new PlatformContextStore(),
            capabilities,
            new NoopEntitlements(),
            new NoopEvents(),
            new ProviderCatalogue([], [], [], []));
    }

    private sealed class FakeCheckProvider : ICheckProvider
    {
        public string Id => "fake-checks";
        public Task<IReadOnlyList<CheckResult>> GetChecksAsync(Guid changeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CheckResult>>([]);
    }

    private sealed class NoopEvents : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : IDomainEvent =>
            Task.CompletedTask;
    }

    private sealed class NoopEntitlements : IEntitlementService
    {
        public EntitlementLevel GetEntitlement(Guid organisationId, string moduleId) => EntitlementLevel.Community;
        public bool HasEntitlement(Guid organisationId, string moduleId, EntitlementLevel required) => required == EntitlementLevel.Community;
        public void Require(Guid organisationId, string moduleId, EntitlementLevel required) { }
        public SeatCapacityStatus GetSeatStatus(Guid organisationId) => new(null, 0, false, null);
        public MaintenanceStatus GetMaintenanceStatus() => new(null, null, false, true);
    }
}
