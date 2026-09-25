using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Context;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Deploy;
using ForgeDeck.Deploy.Application;
using ForgeDeck.Deploy.Domain;
using ForgeDeck.Deploy.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Tests;

public sealed class DeployServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Create_environment_succeeds_for_first_environment()
    {
        using var harness = CreateHarness();
        var env = harness.Service.CreateEnvironment(ProjectId, "production", "Prod");
        Assert.Equal("production", env.Name);
        Assert.Equal("Prod", env.Description);
        Assert.Single(harness.Service.ListEnvironments(ProjectId));
    }

    [Fact]
    public void Community_blocks_second_environment_without_MultiEnvironment()
    {
        using var harness = CreateHarness();
        harness.Service.CreateEnvironment(ProjectId, "production");
        var ex = Assert.Throws<LicenceRequiredException>(() =>
            harness.Service.CreateEnvironment(ProjectId, "staging"));
        Assert.Equal(KnownCapabilities.Deploy.MultiEnvironment, ex.Capability);
        Assert.Equal(1, CommunityLimits.DeployMaxEnvironments);
    }

    [Fact]
    public void Team_capability_allows_multiple_environments()
    {
        using var harness = CreateHarness(withTeam: true);
        harness.Service.CreateEnvironment(ProjectId, "production");
        var staging = harness.Service.CreateEnvironment(ProjectId, "staging");
        Assert.Equal("staging", staging.Name);
        Assert.Equal(2, harness.Service.ListEnvironments(ProjectId).Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_environment_rejects_blank_name(string? name)
    {
        using var harness = CreateHarness();
        Assert.Throws<ArgumentException>(() => harness.Service.CreateEnvironment(ProjectId, name!));
    }

    [Fact]
    public void Delete_environment_removes_it()
    {
        using var harness = CreateHarness();
        var env = harness.Service.CreateEnvironment(ProjectId, "ephemeral");
        harness.Service.DeleteEnvironment(env.Id);
        Assert.Empty(harness.Service.ListEnvironments(ProjectId));
    }

    [Fact]
    public void Delete_missing_environment_throws()
    {
        using var harness = CreateHarness();
        Assert.Throws<KeyNotFoundException>(() => harness.Service.DeleteEnvironment(Guid.NewGuid()));
    }

    [Fact]
    public void Create_deployment_requires_existing_environment()
    {
        using var harness = CreateHarness();
        Assert.Throws<KeyNotFoundException>(() =>
            harness.Service.CreateDeployment(ProjectId, Guid.NewGuid(), "1.0.0", "maya"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_deployment_rejects_blank_version(string? version)
    {
        using var harness = CreateHarness();
        var env = harness.Service.CreateEnvironment(ProjectId, "production");
        Assert.Throws<ArgumentException>(() =>
            harness.Service.CreateDeployment(ProjectId, env.Id, version!, "maya"));
    }

    [Fact]
    public void Create_deployment_succeeds_and_lists()
    {
        using var harness = CreateHarness();
        var env = harness.Service.CreateEnvironment(ProjectId, "production");
        var deployment = harness.Service.CreateDeployment(ProjectId, env.Id, "1.2.3", "maya", "release notes");
        Assert.Equal("1.2.3", deployment.Version);
        Assert.Equal(DeploymentStatus.Succeeded, deployment.Status);
        Assert.Equal("maya", deployment.TriggeredBy);
        Assert.Equal("release notes", deployment.Notes);
        Assert.Contains(harness.Service.ListDeployments(ProjectId), d => d.Id == deployment.Id);
        Assert.Contains(harness.Service.ListDeployments(environmentId: env.Id), d => d.Id == deployment.Id);
    }

    [Fact]
    public void Rollback_marks_previous_and_creates_new_deployment()
    {
        using var harness = CreateHarness();
        var env = harness.Service.CreateEnvironment(ProjectId, "production");
        var original = harness.Service.CreateDeployment(ProjectId, env.Id, "1.0.0", "maya");
        var rollback = harness.Service.Rollback(original.Id, "sam");
        Assert.Equal(original.Id, rollback.RollbackOfId);
        Assert.Equal("1.0.0", rollback.Version);
        Assert.Equal("sam", rollback.TriggeredBy);
        Assert.Equal(DeploymentStatus.Succeeded, rollback.Status);
        Assert.Equal(DeploymentStatus.RolledBack, harness.Service.FindDeployment(original.Id)!.Status);
    }

    [Fact]
    public void Rollback_twice_throws()
    {
        using var harness = CreateHarness();
        var env = harness.Service.CreateEnvironment(ProjectId, "production");
        var original = harness.Service.CreateDeployment(ProjectId, env.Id, "1.0.0", "maya");
        harness.Service.Rollback(original.Id, "sam");
        Assert.Throws<InvalidOperationException>(() => harness.Service.Rollback(original.Id, "sam"));
    }

    [Fact]
    public void Rollback_missing_throws()
    {
        using var harness = CreateHarness();
        Assert.Throws<KeyNotFoundException>(() => harness.Service.Rollback(Guid.NewGuid(), "maya"));
    }

    [Fact]
    public void Find_environment_and_deployment_return_null_when_missing()
    {
        using var harness = CreateHarness();
        Assert.Null(harness.Service.FindEnvironment(Guid.NewGuid()));
        Assert.Null(harness.Service.FindDeployment(Guid.NewGuid()));
    }

    [Fact]
    public void Domain_service_id_is_deploy() =>
        Assert.Equal("deploy", new DeployDomainService().ServiceId);

    [Theory]
    [InlineData("alpha")]
    [InlineData("beta")]
    [InlineData("gamma")]
    [InlineData("delta")]
    [InlineData("epsilon")]
    public void Team_can_create_named_environments(string name)
    {
        using var harness = CreateHarness(withTeam: true);
        var env = harness.Service.CreateEnvironment(ProjectId, name);
        Assert.Equal(name, env.Name);
    }

    private static Harness CreateHarness(bool withTeam = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"forgedeck-deploy-{Guid.NewGuid():N}.db");
        var connections = new SqliteConnectionFactory($"Data Source={path}");
        var schema = new DeploySchemaInitializer(connections);
        var store = new SqliteDeployStore(connections, schema);

        OrganisationLicenceEntitlement? entitlement = null;
        IPlatformModule[] modules = [new DeployModule()];
        if (withTeam)
        {
            var keys = LicenceCryptography.CreateKeyPair();
            var document = LicenceSkuCatalog.CreateDocument("DEPLOY-TEAM", KnownIds.OrganisationId);
            document.Signature = LicenceCryptography.Sign(document, keys);
            var json = System.Text.Json.JsonSerializer.Serialize(
                document,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            entitlement = SignedLicenceEntitlementStore.ParseVerified(json, keys);
            modules = [new DeployModule(), new FakeDeployTeamModule()];
        }

        var capabilities = new CapabilityService(modules, new SignedLicenceEntitlementStore(entitlement));
        var context = new PlatformContextStore();
        var service = new DeployService(store, capabilities, context);
        return new Harness(path, service);
    }

    private sealed class FakeDeployTeamModule : IPlatformModule
    {
        public ModuleManifest Manifest { get; } = new(
            "deploy-team", "Deploy Team", "0.1.0", "Team",
            [KnownCapabilities.Deploy.MultiEnvironment], [], []);

        public void RegisterServices(IServiceCollection services, IConfiguration configuration) { }
        public void MapEndpoints(IEndpointRouteBuilder endpoints) { }
    }

    private sealed class Harness(string path, DeployService service) : IDisposable
    {
        public DeployService Service { get; } = service;
        public void Dispose()
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
