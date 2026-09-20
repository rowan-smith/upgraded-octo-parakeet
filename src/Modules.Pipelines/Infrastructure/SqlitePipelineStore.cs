using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Modules.Pipelines.Application;
using Modules.Pipelines.Domain;
using Platform.Core.Persistence;

namespace Modules.Pipelines.Infrastructure;

public sealed class SqlitePipelineStore : IPipelineStore
{
    public static readonly Guid DotNetValidationDefinitionId = Guid.Parse("10000000-0000-4000-8000-000000000001");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connections;
    private readonly object _seedGate = new();
    private bool _seeded;

    public SqlitePipelineStore(IDbConnectionFactory connections, PipelineSchemaInitializer schema)
    {
        _connections = connections;
        schema.EnsureCreated();
        EnsureSeeded();
    }

    public IReadOnlyList<PipelineDefinition> ListDefinitions(Guid? projectId = null)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        if (projectId is null)
            command.CommandText = "SELECT payload FROM pipelines_definitions ORDER BY name";
        else
        {
            command.CommandText = "SELECT payload FROM pipelines_definitions WHERE project_id=$project ORDER BY name";
            Add(command, "$project", projectId.Value.ToString());
        }
        using var reader = command.ExecuteReader();
        var result = new List<PipelineDefinition>();
        while (reader.Read()) result.Add(Deserialize<PipelineDefinition>(reader.GetString(0)));
        return result;
    }

    public PipelineDefinition? FindDefinition(Guid id)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM pipelines_definitions WHERE id=$id";
        Add(command, "$id", id.ToString());
        return command.ExecuteScalar() is string payload ? Deserialize<PipelineDefinition>(payload) : null;
    }

    public void SaveDefinition(Guid projectId, PipelineDefinition definition, bool recordVersion = true)
    {
        var payload = Serialize(definition);
        using var connection = _connections.Open();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO pipelines_definitions(id, project_id, name, enabled, version, payload, updated_at)
                VALUES($id, $project, $name, $enabled, $version, $payload, $updated)
                ON CONFLICT(id) DO UPDATE SET
                    name=excluded.name,
                    enabled=excluded.enabled,
                    version=excluded.version,
                    payload=excluded.payload,
                    updated_at=excluded.updated_at
                """;
            Add(command, "$id", definition.Id.ToString());
            Add(command, "$project", projectId.ToString());
            Add(command, "$name", definition.Name);
            Add(command, "$enabled", definition.Enabled ? 1 : 0);
            Add(command, "$version", definition.Version);
            Add(command, "$payload", payload);
            Add(command, "$updated", definition.UpdatedAt.ToString("O"));
            command.ExecuteNonQuery();
        }

        if (recordVersion)
        {
            using var versionCommand = connection.CreateCommand();
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = """
                INSERT INTO pipelines_definition_versions(definition_id, version, payload, created_at)
                VALUES($id, $version, $payload, $created)
                ON CONFLICT(definition_id, version) DO UPDATE SET payload=excluded.payload
                """;
            Add(versionCommand, "$id", definition.Id.ToString());
            Add(versionCommand, "$version", definition.Version);
            Add(versionCommand, "$payload", payload);
            Add(versionCommand, "$created", DateTimeOffset.UtcNow.ToString("O"));
            versionCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void DeleteDefinition(Guid definitionId)
    {
        using var connection = _connections.Open();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM pipelines_definition_versions WHERE definition_id=$id";
            Add(command, "$id", definitionId.ToString());
            command.ExecuteNonQuery();
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM pipelines_definitions WHERE id=$id";
            Add(command, "$id", definitionId.ToString());
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public string? GetDefinitionVersionPayload(Guid definitionId, int version)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM pipelines_definition_versions WHERE definition_id=$id AND version=$version";
        Add(command, "$id", definitionId.ToString());
        Add(command, "$version", version);
        return command.ExecuteScalar() as string;
    }

    public IReadOnlyList<PipelineRun> ListRuns(int take = 100)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM pipelines_runs ORDER BY created_at DESC LIMIT $take";
        Add(command, "$take", take);
        using var reader = command.ExecuteReader();
        var result = new List<PipelineRun>();
        while (reader.Read()) result.Add(Deserialize<PipelineRun>(reader.GetString(0)));
        return result;
    }

    public PipelineRun? FindRun(Guid id)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM pipelines_runs WHERE id=$id";
        Add(command, "$id", id.ToString());
        return command.ExecuteScalar() is string payload ? Deserialize<PipelineRun>(payload) : null;
    }

    public IReadOnlyList<PipelineRun> FindRunsForChange(Guid changeId)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM pipelines_runs WHERE change_id=$change ORDER BY created_at DESC";
        Add(command, "$change", changeId.ToString());
        using var reader = command.ExecuteReader();
        var result = new List<PipelineRun>();
        while (reader.Read()) result.Add(Deserialize<PipelineRun>(reader.GetString(0)));
        return result;
    }

    public IReadOnlyList<PipelineRun> FindActiveRunsForChange(Guid changeId, Guid definitionId)
    {
        return FindRunsForChange(changeId)
            .Where(run => run.DefinitionId == definitionId &&
                          run.Status is PipelineRunStatus.Queued or PipelineRunStatus.Running &&
                          !run.IsSuperseded)
            .ToArray();
    }

    public void SaveRun(PipelineRun run)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pipelines_runs(id, definition_id, change_id, commit_sha, status, payload, created_at)
            VALUES($id, $definition, $change, $sha, $status, $payload, $created)
            ON CONFLICT(id) DO UPDATE SET
                change_id=excluded.change_id,
                commit_sha=excluded.commit_sha,
                status=excluded.status,
                payload=excluded.payload
            """;
        Add(command, "$id", run.Id.ToString());
        Add(command, "$definition", run.DefinitionId.ToString());
        Add(command, "$change", (object?)run.ChangeId?.ToString() ?? DBNull.Value);
        Add(command, "$sha", run.CommitSha);
        Add(command, "$status", run.Status.ToString());
        Add(command, "$payload", Serialize(run));
        Add(command, "$created", run.CreatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<PipelineRun> ListRunsWithJobsWaitingForRunner()
    {
        return ListRuns(500)
            .Where(run => run.Status is PipelineRunStatus.Queued or PipelineRunStatus.Running &&
                          run.Jobs.Any(job => job.Status == JobStatus.WaitingForRunner))
            .ToArray();
    }

    public IReadOnlyList<RunnerAgent> ListRunners()
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM pipelines_runners ORDER BY name";
        using var reader = command.ExecuteReader();
        var result = new List<RunnerAgent>();
        while (reader.Read()) result.Add(Deserialize<RunnerAgent>(reader.GetString(0)));
        return result;
    }

    public RunnerAgent? FindRunner(Guid id)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM pipelines_runners WHERE id=$id";
        Add(command, "$id", id.ToString());
        return command.ExecuteScalar() is string payload ? Deserialize<RunnerAgent>(payload) : null;
    }

    public RunnerAgent? FindRunnerByTokenHash(string tokenHash)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM pipelines_runners WHERE token_hash=$hash";
        Add(command, "$hash", tokenHash);
        return command.ExecuteScalar() is string payload ? Deserialize<RunnerAgent>(payload) : null;
    }

    public void SaveRunner(RunnerAgent runner)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pipelines_runners(id, name, token_hash, status, payload, last_heartbeat)
            VALUES($id, $name, $hash, $status, $payload, $heartbeat)
            ON CONFLICT(id) DO UPDATE SET
                name=excluded.name,
                token_hash=excluded.token_hash,
                status=excluded.status,
                payload=excluded.payload,
                last_heartbeat=excluded.last_heartbeat
            """;
        Add(command, "$id", runner.Id.ToString());
        Add(command, "$name", runner.Name);
        Add(command, "$hash", runner.TokenHash);
        Add(command, "$status", runner.Status.ToString());
        Add(command, "$payload", Serialize(runner));
        Add(command, "$heartbeat", (object?)runner.LastHeartbeatAt?.ToString("O") ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void RevokeRunner(Guid runnerId)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM pipelines_runners WHERE id=$id";
        Add(command, "$id", runnerId.ToString());
        command.ExecuteNonQuery();
    }

    public void SaveRegistrationToken(RunnerRegistrationToken token)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pipelines_registration_tokens(id, token_hash, expires_at, consumed_at, created_by, created_at)
            VALUES($id, $hash, $expires, $consumed, $created_by, $created)
            ON CONFLICT(id) DO UPDATE SET consumed_at=excluded.consumed_at
            """;
        Add(command, "$id", token.Id.ToString());
        Add(command, "$hash", token.TokenHash);
        Add(command, "$expires", token.ExpiresAt.ToString("O"));
        Add(command, "$consumed", (object?)token.ConsumedAt?.ToString("O") ?? DBNull.Value);
        Add(command, "$created_by", token.CreatedBy);
        Add(command, "$created", token.CreatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    public RunnerRegistrationToken? FindRegistrationTokenByHash(string tokenHash)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, token_hash, expires_at, consumed_at, created_by, created_at
            FROM pipelines_registration_tokens WHERE token_hash=$hash
            """;
        Add(command, "$hash", tokenHash);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new RunnerRegistrationToken
        {
            Id = Guid.Parse(reader.GetString(0)),
            TokenHash = reader.GetString(1),
            ExpiresAt = DateTimeOffset.Parse(reader.GetString(2)),
            ConsumedAt = reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
            CreatedBy = reader.GetString(4),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(5))
        };
    }

    public void ConsumeRegistrationToken(Guid tokenId)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE pipelines_registration_tokens SET consumed_at=$consumed WHERE id=$id";
        Add(command, "$consumed", DateTimeOffset.UtcNow.ToString("O"));
        Add(command, "$id", tokenId.ToString());
        command.ExecuteNonQuery();
    }

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private void EnsureSeeded()
    {
        if (_seeded) return;
        lock (_seedGate)
        {
            if (_seeded) return;
            var projectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var seed = CreateDotNetValidationDefinition();
            var existing = FindDefinition(DotNetValidationDefinitionId);
            // Jobs run in isolated workspaces — refresh built-in definition when outdated.
            if (existing is null || existing.Version < seed.Version)
                SaveDefinition(projectId, seed);
            _seeded = true;
        }
    }

    public static PipelineDefinition CreateDotNetValidationDefinition()
    {
        static PipelineStepDefinition Step(string name, string command, int timeout) =>
            new(name, command, null, new Dictionary<string, string>(), timeout);

        static IReadOnlyList<PipelineStepDefinition> RestoreAndBuild(int restoreTimeout, int buildTimeout) =>
        [
            Step("Restore packages", "dotnet restore", restoreTimeout),
            Step("Build solution", "dotnet build --no-restore", buildTimeout)
        ];

        return new()
        {
            Id = DotNetValidationDefinitionId,
            Name = ".NET Validation",
            Enabled = true,
            Version = 2,
            Triggers =
            [
                PipelineTrigger.Manual,
                PipelineTrigger.ChangeOpened,
                PipelineTrigger.ChangeUpdated,
                PipelineTrigger.Push
            ],
            Environment = new Dictionary<string, string> { ["DOTNET_NOLOGO"] = "true" },
            TimeoutSeconds = 3600,
            Jobs =
            [
                new PipelineJobDefinition(
                    "Build",
                    RestoreAndBuild(600, 900),
                    ["dotnet"],
                    new Dictionary<string, string>(),
                    1500,
                    PublishCheck: true,
                    CheckName: "Build",
                    ArtifactGlobs: [],
                    ContinueOnError: false),
                new PipelineJobDefinition(
                    "Unit Tests",
                    [
                        ..RestoreAndBuild(600, 900),
                        Step(
                            "Run unit tests",
                            "dotnet test tests/Tests.Unit --no-build --logger \"trx;LogFileName=unit.trx\" --results-directory TestResults",
                            1200)
                    ],
                    ["dotnet"],
                    new Dictionary<string, string>(),
                    2700,
                    PublishCheck: true,
                    CheckName: "Unit Tests",
                    ArtifactGlobs: ["TestResults/**/*.trx"],
                    ContinueOnError: false),
                new PipelineJobDefinition(
                    "Integration Tests",
                    [
                        ..RestoreAndBuild(600, 900),
                        Step(
                            "Run integration tests",
                            "dotnet test tests/Tests.Integration --no-build --logger \"trx;LogFileName=integration.trx\" --results-directory TestResults",
                            1800)
                    ],
                    ["dotnet"],
                    new Dictionary<string, string>(),
                    3300,
                    PublishCheck: true,
                    CheckName: "Integration Tests",
                    ArtifactGlobs: ["TestResults/**/*.trx"],
                    ContinueOnError: false),
                new PipelineJobDefinition(
                    "E2E Tests",
                    [
                        ..RestoreAndBuild(600, 900),
                        Step(
                            "Run E2E tests",
                            "dotnet test tests/Tests.E2E --no-build --logger \"trx;LogFileName=e2e.trx\" --results-directory TestResults",
                            1800)
                    ],
                    ["dotnet"],
                    new Dictionary<string, string>(),
                    3300,
                    PublishCheck: true,
                    CheckName: "E2E Tests",
                    ArtifactGlobs: ["TestResults/**/*.trx"],
                    ContinueOnError: false)
            ]
        };
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T Deserialize<T>(string payload) => JsonSerializer.Deserialize<T>(payload, Json) ?? throw new InvalidOperationException("Stored pipeline payload is invalid.");

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
