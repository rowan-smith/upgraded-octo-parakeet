using System.Text.Json;
using ForgeDeck.Deploy.Application;
using ForgeDeck.Deploy.Domain;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Deploy.Infrastructure;

public sealed class EfDeployStore : IDeployStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<DeployDbContext> _dbFactory;
    private readonly object _schemaGate = new();
    private bool _schemaReady;

    public EfDeployStore(IDbContextFactory<DeployDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
        EnsureSchema();
    }

    public IReadOnlyList<DeploymentEnvironment> ListEnvironments(Guid? projectId = null)
    {
        using var db = _dbFactory.CreateDbContext();
        var query = db.Environments.AsNoTracking();
        if (projectId is not null)
        {
            var project = projectId.Value.ToString();
            query = query.Where(e => e.ProjectId == project);
        }

        return query.OrderBy(e => e.Name).Select(e => e.Payload).AsEnumerable().Select(Deserialize<DeploymentEnvironment>).ToList();
    }

    public DeploymentEnvironment? FindEnvironment(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        var payload = db.Environments.AsNoTracking().Where(e => e.Id == id.ToString()).Select(e => e.Payload).FirstOrDefault();
        return payload is null ? null : Deserialize<DeploymentEnvironment>(payload);
    }

    public void SaveEnvironment(DeploymentEnvironment environment)
    {
        using var db = _dbFactory.CreateDbContext();
        var id = environment.Id.ToString();
        var row = db.Environments.FirstOrDefault(e => e.Id == id);
        if (row is null)
        {
            row = new DeployEnvironmentRow
            {
                Id = id,
                CreatedAt = environment.CreatedAt.ToString("O")
            };
            db.Environments.Add(row);
        }

        row.ProjectId = environment.ProjectId.ToString();
        row.Name = environment.Name;
        row.Payload = JsonSerializer.Serialize(environment, Json);
        db.SaveChanges();
    }

    public void DeleteEnvironment(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        db.Environments.Where(e => e.Id == id.ToString()).ExecuteDelete();
    }

    public IReadOnlyList<Deployment> ListDeployments(Guid? projectId = null, Guid? environmentId = null, int take = 100)
    {
        using var db = _dbFactory.CreateDbContext();
        var query = db.Deployments.AsNoTracking();
        if (environmentId is not null)
        {
            var env = environmentId.Value.ToString();
            query = query.Where(d => d.EnvironmentId == env);
        }
        else if (projectId is not null)
        {
            var project = projectId.Value.ToString();
            query = query.Where(d => d.ProjectId == project);
        }

        return query.OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .Select(d => d.Payload)
            .AsEnumerable()
            .Select(Deserialize<Deployment>)
            .ToList();
    }

    public Deployment? FindDeployment(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        var payload = db.Deployments.AsNoTracking().Where(d => d.Id == id.ToString()).Select(d => d.Payload).FirstOrDefault();
        return payload is null ? null : Deserialize<Deployment>(payload);
    }

    public void SaveDeployment(Deployment deployment)
    {
        using var db = _dbFactory.CreateDbContext();
        var id = deployment.Id.ToString();
        var row = db.Deployments.FirstOrDefault(d => d.Id == id);
        if (row is null)
        {
            row = new DeployDeploymentRow
            {
                Id = id,
                CreatedAt = deployment.CreatedAt.ToString("O")
            };
            db.Deployments.Add(row);
        }

        row.ProjectId = deployment.ProjectId.ToString();
        row.EnvironmentId = deployment.EnvironmentId.ToString();
        row.Status = deployment.Status.ToString();
        row.Payload = JsonSerializer.Serialize(deployment, Json);
        db.SaveChanges();
    }

    public void SaveBuildRunReference(Guid runId, string pipelineName, string commitSha, Guid? changeId)
    {
        var reference = new DeployBuildRunReference(runId, pipelineName, commitSha, changeId, DateTimeOffset.UtcNow);
        using var db = _dbFactory.CreateDbContext();
        var id = runId.ToString();
        var row = db.BuildRuns.FirstOrDefault(r => r.Id == id);
        if (row is null)
        {
            row = new DeployBuildRunRow
            {
                Id = id,
                CreatedAt = reference.RecordedAt.ToString("O")
            };
            db.BuildRuns.Add(row);
        }

        row.PipelineName = pipelineName;
        row.CommitSha = commitSha;
        row.ChangeId = changeId?.ToString();
        row.Payload = JsonSerializer.Serialize(reference, Json);
        db.SaveChanges();
    }

    public void SaveArtifactReference(Guid runId, string pipelineName, string artifactName, string? uri)
    {
        var reference = new DeployArtifactReference(runId, pipelineName, artifactName, uri, DateTimeOffset.UtcNow);
        using var db = _dbFactory.CreateDbContext();
        var id = $"{runId:N}:{artifactName}";
        var row = db.Artifacts.FirstOrDefault(a => a.Id == id);
        if (row is null)
        {
            row = new DeployArtifactRow
            {
                Id = id,
                CreatedAt = reference.RecordedAt.ToString("O")
            };
            db.Artifacts.Add(row);
        }

        row.RunId = runId.ToString();
        row.PipelineName = pipelineName;
        row.ArtifactName = artifactName;
        row.Payload = JsonSerializer.Serialize(reference, Json);
        db.SaveChanges();
    }

    public IReadOnlyList<DeployBuildRunReference> ListBuildRunReferences(int take = 50)
    {
        using var db = _dbFactory.CreateDbContext();
        return db.BuildRuns.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .Select(r => r.Payload)
            .AsEnumerable()
            .Select(Deserialize<DeployBuildRunReference>)
            .ToList();
    }

    public IReadOnlyList<DeployArtifactReference> ListArtifactReferences(Guid? runId = null, int take = 50)
    {
        using var db = _dbFactory.CreateDbContext();
        var query = db.Artifacts.AsNoTracking();
        if (runId is not null)
        {
            var id = runId.Value.ToString();
            query = query.Where(a => a.RunId == id);
        }

        return query.OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .Select(a => a.Payload)
            .AsEnumerable()
            .Select(Deserialize<DeployArtifactReference>)
            .ToList();
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

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidOperationException("Corrupt deploy payload.");
}
