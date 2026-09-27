using System.Text.Json;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Context;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Deploy;
using ForgeDeck.Deploy.Application;
using ForgeDeck.Deploy.Infrastructure;
using ForgeDeck.Deploy.Team;
using Microsoft.EntityFrameworkCore;

namespace Deploy.Team.Tests;

public sealed class DeployTeamTests
{
    private static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProjectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Team_depends_on_community_deploy()
    {
        var refs = typeof(DeployTeamModule).Assembly.GetReferencedAssemblies().Select(r => r.Name).ToArray();
        Assert.Contains("ForgeDeck.Deploy", refs);
        Assert.DoesNotContain("ForgeDeck.Deploy.Enterprise", refs);
    }

    [Fact]
    public void Team_package_without_licence_does_not_grant_multi_environment()
    {
        var service = new CapabilityService(
            [new DeployModule(), new DeployTeamModule()],
            new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null));

        Assert.False(service.Has(OrganisationId, KnownCapabilities.Deploy.MultiEnvironment));
    }

    [Fact]
    public void Team_licence_with_package_grants_multi_environment()
    {
        var keys = LicenceCryptography.CreateKeyPair();
        var document = LicenceSkuCatalog.CreateDocument("DEPLOY-TEAM", OrganisationId);
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var entitlement = SignedLicenceEntitlementStore.ParseVerified(json, keys)!;
        var service = new CapabilityService(
            [new DeployModule(), new DeployTeamModule()],
            new SignedLicenceEntitlementStore(entitlement));

        Assert.True(service.Has(OrganisationId, KnownCapabilities.Deploy.MultiEnvironment));
        Assert.False(service.Has(OrganisationId, KnownCapabilities.Deploy.PromotionPolicy));
    }

    [Fact]
    public void Licensed_multi_environment_allows_more_than_one_environment()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forgedeck-deploy-team-{Guid.NewGuid():N}.db");
        try
        {
            var keys = LicenceCryptography.CreateKeyPair();
            var document = LicenceSkuCatalog.CreateDocument("DEPLOY-TEAM", KnownIds.OrganisationId);
            document.Signature = LicenceCryptography.Sign(document, keys);
            var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var entitlement = SignedLicenceEntitlementStore.ParseVerified(json, keys)!;

            var factory = new TestDeployDbContextFactory($"Data Source={path}");
            var store = new EfDeployStore(factory);
            var capabilities = new CapabilityService(
                [new DeployModule(), new DeployTeamModule()],
                new SignedLicenceEntitlementStore(entitlement));
            var context = new PlatformContextStore();
            var publisher = new NoopPublisher();
            var service = new DeployService(store, capabilities, context, publisher, new ImmediateDeploymentExecutor(store, publisher));

            service.CreateEnvironment(ProjectId, "production");
            service.CreateEnvironment(ProjectId, "staging");
            service.CreateEnvironment(ProjectId, "qa");
            Assert.Equal(3, service.ListEnvironments(ProjectId).Count);
            Assert.Null(SoftLimits.MaxEnvironments(true));
            Assert.Equal(CommunityLimits.DeployMaxEnvironments, SoftLimits.MaxEnvironments(false));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    private sealed class TestDeployDbContextFactory(string connectionString) : IDbContextFactory<DeployDbContext>
    {
        public DeployDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<DeployDbContext>().UseSqlite(connectionString).Options;
            return new DeployDbContext(options);
        }
    }

    private sealed class NoopPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent data, PublishOptions? options = null, CancellationToken cancellationToken = default)
            where TEvent : class =>
            Task.CompletedTask;
    }
}
