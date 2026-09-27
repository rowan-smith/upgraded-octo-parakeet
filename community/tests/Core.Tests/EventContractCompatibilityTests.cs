using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Deploy.Contracts.Events;
using ForgeDeck.Git.Contracts.Events;
using ForgeDeck.Review.Contracts.Events;
using Microsoft.Extensions.Configuration;

namespace Core.Tests;

public sealed class EventContractCompatibilityTests
{
    [Fact]
    public void Canonical_module_events_have_stable_type_and_positive_version()
    {
        IEventContract[] contracts =
        [
            ..GitEventContracts.All(),
            ..ReviewEventContracts.All(),
            ..BuildEventContracts.All(),
            ..DeployEventContracts.All()
        ];

        Assert.NotEmpty(contracts);
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in contracts)
        {
            Assert.False(string.IsNullOrWhiteSpace(contract.Type));
            Assert.StartsWith("forgedeck.", contract.Type, StringComparison.Ordinal);
            Assert.True(contract.Version >= 1);
            Assert.True(types.Add($"{contract.Type}@{contract.Version}"),
                $"Duplicate contract registration: {contract.Type} v{contract.Version}");
        }
    }

    [Fact]
    public void Build_pipeline_run_contracts_use_build_prefix_not_pipelines()
    {
        foreach (var contract in BuildEventContracts.All())
        {
            Assert.StartsWith("forgedeck.build.", contract.Type, StringComparison.Ordinal);
            Assert.DoesNotContain("pipelines", contract.Type, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Unsupported_version_is_detectable_by_registry_miss()
    {
        var registry = new InMemoryEventRegistry();
        foreach (var contract in BuildEventContracts.All())
        {
            registry.Register(contract);
        }

        Assert.NotNull(registry.TryGet(BuildEventContracts.PipelineRunStarted.Type, 1));
        Assert.Null(registry.TryGet(BuildEventContracts.PipelineRunStarted.Type, 999));
    }
}

public sealed class DatabaseProviderParityTests
{
    [Fact]
    public void Sqlite_and_postgresql_provider_resolution_are_explicit()
    {
        var sqlite = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "Sqlite" })
            .Build();
        var pg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "PostgreSql" })
            .Build();
        var legacy = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "Postgres" })
            .Build();

        Assert.Equal(DatabaseProvider.Sqlite, DatabaseProvider.Resolve(sqlite));
        Assert.Equal(DatabaseProvider.PostgreSql, DatabaseProvider.Resolve(pg));
        Assert.Equal(DatabaseProvider.PostgreSql, DatabaseProvider.Resolve(legacy));
        Assert.False(DatabaseProvider.IsPostgreSql(sqlite));
        Assert.True(DatabaseProvider.IsPostgreSql(pg));
    }

    [Fact]
    public void Soft_limits_are_provider_independent()
    {
        Assert.Equal(1, SoftLimits.MaxConcurrentPipelines(hasBuildConcurrent: false));
        Assert.Null(SoftLimits.MaxConcurrentPipelines(hasBuildConcurrent: true));
        Assert.Equal(1, SoftLimits.MaxEnvironments(hasMultiEnvironment: false));
        Assert.Null(SoftLimits.MaxEnvironments(hasMultiEnvironment: true));
    }
}

/// <summary>Minimal in-memory registry for contract lookup tests.</summary>
file sealed class InMemoryEventRegistry : IEventRegistry
{
    private readonly Dictionary<(string Type, int Version), IEventContract> _contracts = new();

    public void Register(IEventContract contract) =>
        _contracts[(contract.Type, contract.Version)] = contract;

    public IEventContract GetRequired(string type, int version) =>
        TryGet(type, version)
        ?? throw new InvalidOperationException($"Event contract '{type}' v{version} is not registered.");

    public IEventContract? TryGet(string type, int version) =>
        _contracts.TryGetValue((type, version), out var contract) ? contract : null;

    public IEventContract? TryGetByClrType(Type clrType) =>
        _contracts.Values.FirstOrDefault(c => c.ClrType == clrType);

    public IReadOnlyList<IEventContract> List() => _contracts.Values.ToArray();
}
