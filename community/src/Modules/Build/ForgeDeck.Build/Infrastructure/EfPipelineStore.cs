using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeDeck.Build.Application;
using ForgeDeck.Build.Domain;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Build.Infrastructure;

public sealed class EfPipelineStore : IPipelineStore
{
    public static readonly Guid DotNetValidationDefinitionId = Guid.Parse("10000000-0000-4000-8000-000000000001");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<BuildDbContext> _dbFactory;
    private readonly object _schemaGate = new();
    private readonly object _seedGate = new();
    private bool _schemaReady;
    private bool _seeded;

    public EfPipelineStore(IDbContextFactory<BuildDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
        EnsureSchema();
        EnsureSeeded();
    }

    public IReadOnlyList<PipelineDefinition> ListDefinitions(Guid? projectId = null)
    {
        using var db = _dbFactory.CreateDbContext();
        var query = db.Definitions.AsNoTracking();
        if (projectId is not null)
        {
            var project = projectId.Value.ToString();
            query = query.Where(d => d.ProjectId == project);
        }

        return query.OrderBy(d => d.Name).Select(d => d.Payload).AsEnumerable().Select(Deserialize<PipelineDefinition>).ToList();
    }

    public PipelineDefinition? FindDefinition(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        var payload = db.Definitions.AsNoTracking().Where(d => d.Id == id.ToString()).Select(d => d.Payload).FirstOrDefault();
        return payload is null ? null : Deserialize<PipelineDefinition>(payload);
    }

    public void SaveDefinition(Guid projectId, PipelineDefinition definition, bool recordVersion = true)
    {
        var payload = Serialize(definition);
        using var db = _dbFactory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        var id = definition.Id.ToString();
        var row = db.Definitions.FirstOrDefault(d => d.Id == id);
        if (row is null)
        {
            row = new PipelineDefinitionRow { Id = id };
            db.Definitions.Add(row);
        }

        row.ProjectId = projectId.ToString();
        row.Name = definition.Name;
        row.Enabled = definition.Enabled ? 1 : 0;
        row.Version = definition.Version;
        row.Payload = payload;
        row.UpdatedAt = definition.UpdatedAt.ToString("O");

        if (recordVersion)
        {
            var version = db.DefinitionVersions.FirstOrDefault(v => v.DefinitionId == id && v.Version == definition.Version);
            if (version is null)
            {
                db.DefinitionVersions.Add(new PipelineDefinitionVersionRow
                {
                    DefinitionId = id,
                    Version = definition.Version,
                    Payload = payload,
                    CreatedAt = DateTimeOffset.UtcNow.ToString("O")
                });
            }
            else
            {
                version.Payload = payload;
            }
        }

        db.SaveChanges();
        transaction.Commit();
    }

    public void DeleteDefinition(Guid definitionId)
    {
        using var db = _dbFactory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        var id = definitionId.ToString();
        db.DefinitionVersions.Where(v => v.DefinitionId == id).ExecuteDelete();
        db.Definitions.Where(d => d.Id == id).ExecuteDelete();
        transaction.Commit();
    }

    public string? GetDefinitionVersionPayload(Guid definitionId, int version)
    {
        using var db = _dbFactory.CreateDbContext();
        return db.DefinitionVersions.AsNoTracking()
            .Where(v => v.DefinitionId == definitionId.ToString() && v.Version == version)
            .Select(v => v.Payload)
            .FirstOrDefault();
    }

    public IReadOnlyList<PipelineRun> ListRuns(int take = 100)
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Runs.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .Select(r => r.Payload)
            .AsEnumerable()
            .Select(Deserialize<PipelineRun>)
            .ToList();
    }

    public PipelineRun? FindRun(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        var payload = db.Runs.AsNoTracking().Where(r => r.Id == id.ToString()).Select(r => r.Payload).FirstOrDefault();
        return payload is null ? null : Deserialize<PipelineRun>(payload);
    }

    public PipelineRun? FindRunByIdempotencyKey(Guid idempotencyKey) =>
        ListRuns(500).FirstOrDefault(run => run.IdempotencyKey == idempotencyKey);

    public IReadOnlyList<PipelineRun> FindRunsForChange(Guid changeId)
    {
        using var db = _dbFactory.CreateDbContext();
        var change = changeId.ToString();
        return db.Runs.AsNoTracking()
            .Where(r => r.ChangeId == change)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Payload)
            .AsEnumerable()
            .Select(Deserialize<PipelineRun>)
            .ToList();
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
        using var db = _dbFactory.CreateDbContext();
        var id = run.Id.ToString();
        var row = db.Runs.FirstOrDefault(r => r.Id == id);
        if (row is null)
        {
            row = new PipelineRunRow { Id = id, CreatedAt = run.CreatedAt.ToString("O") };
            db.Runs.Add(row);
        }

        row.DefinitionId = run.DefinitionId.ToString();
        row.ChangeId = run.ChangeId?.ToString();
        row.CommitSha = run.CommitSha;
        row.Status = run.Status.ToString();
        row.Payload = Serialize(run);
        db.SaveChanges();
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
        using var db = _dbFactory.CreateDbContext();
        return db.Runners.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => r.Payload)
            .AsEnumerable()
            .Select(Deserialize<RunnerAgent>)
            .ToList();
    }

    public RunnerAgent? FindRunner(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        var payload = db.Runners.AsNoTracking().Where(r => r.Id == id.ToString()).Select(r => r.Payload).FirstOrDefault();
        return payload is null ? null : Deserialize<RunnerAgent>(payload);
    }

    public RunnerAgent? FindRunnerByTokenHash(string tokenHash)
    {
        using var db = _dbFactory.CreateDbContext();
        var payload = db.Runners.AsNoTracking().Where(r => r.TokenHash == tokenHash).Select(r => r.Payload).FirstOrDefault();
        return payload is null ? null : Deserialize<RunnerAgent>(payload);
    }

    public void SaveRunner(RunnerAgent runner)
    {
        using var db = _dbFactory.CreateDbContext();
        var id = runner.Id.ToString();
        var row = db.Runners.FirstOrDefault(r => r.Id == id);
        if (row is null)
        {
            row = new PipelineRunnerRow { Id = id };
            db.Runners.Add(row);
        }

        row.Name = runner.Name;
        row.TokenHash = runner.TokenHash;
        row.Status = runner.Status.ToString();
        row.Payload = Serialize(runner);
        row.LastHeartbeat = runner.LastHeartbeatAt?.ToString("O");
        db.SaveChanges();
    }

    public void RevokeRunner(Guid runnerId)
    {
        using var db = _dbFactory.CreateDbContext();
        db.Runners.Where(r => r.Id == runnerId.ToString()).ExecuteDelete();
    }

    public void SaveRegistrationToken(RunnerRegistrationToken token)
    {
        using var db = _dbFactory.CreateDbContext();
        var id = token.Id.ToString();
        var row = db.RegistrationTokens.FirstOrDefault(t => t.Id == id);
        if (row is null)
        {
            row = new PipelineRegistrationTokenRow
            {
                Id = id,
                TokenHash = token.TokenHash,
                ExpiresAt = token.ExpiresAt.ToString("O"),
                CreatedBy = token.CreatedBy,
                CreatedAt = token.CreatedAt.ToString("O")
            };
            db.RegistrationTokens.Add(row);
        }

        row.ConsumedAt = token.ConsumedAt?.ToString("O");
        db.SaveChanges();
    }

    public RunnerRegistrationToken? FindRegistrationTokenByHash(string tokenHash)
    {
        using var db = _dbFactory.CreateDbContext();
        var row = db.RegistrationTokens.AsNoTracking().FirstOrDefault(t => t.TokenHash == tokenHash);
        if (row is null)
        {
            return null;
        }

        return new RunnerRegistrationToken
        {
            Id = Guid.Parse(row.Id),
            TokenHash = row.TokenHash,
            ExpiresAt = DateTimeOffset.Parse(row.ExpiresAt),
            ConsumedAt = row.ConsumedAt is null ? null : DateTimeOffset.Parse(row.ConsumedAt),
            CreatedBy = row.CreatedBy,
            CreatedAt = DateTimeOffset.Parse(row.CreatedAt)
        };
    }

    public void ConsumeRegistrationToken(Guid tokenId)
    {
        using var db = _dbFactory.CreateDbContext();
        var row = db.RegistrationTokens.FirstOrDefault(t => t.Id == tokenId.ToString());
        if (row is null)
        {
            return;
        }

        row.ConsumedAt = DateTimeOffset.UtcNow.ToString("O");
        db.SaveChanges();
    }

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private void EnsureSchema()
    {
        if (_schemaReady)
        {
            return;
        }

        lock (_schemaGate)
        {
            if (_schemaReady)
            {
                return;
            }

            using var db = _dbFactory.CreateDbContext();
            db.EnsureSchema();
            _schemaReady = true;
        }
    }

    private void EnsureSeeded()
    {
        if (_seeded)
        {
            return;
        }

        lock (_seedGate)
        {
            if (_seeded)
            {
                return;
            }

            var projectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var seed = CreateDotNetValidationDefinition();
            var existing = FindDefinition(DotNetValidationDefinitionId);
            // Jobs run in isolated workspaces — refresh built-in definition when outdated.
            if (existing is null || existing.Version < seed.Version)
            {
                SaveDefinition(projectId, seed);
            }

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
                            "dotnet test tests/ce/Core.Tests --no-build --logger \"trx;LogFileName=unit.trx\" --results-directory TestResults",
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
                            "dotnet test tests/ce/Integration.Tests --no-build --logger \"trx;LogFileName=integration.trx\" --results-directory TestResults",
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
                            "dotnet test tests/ce/E2E.Tests --no-build --logger \"trx;LogFileName=e2e.trx\" --results-directory TestResults",
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
    private static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, Json) ?? throw new InvalidOperationException("Stored pipeline payload is invalid.");
}
