using System.Data.Common;
using System.Text.Json;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Deploy.Application;
using ForgeDeck.Deploy.Domain;

namespace ForgeDeck.Deploy.Infrastructure;

public sealed class SqliteDeployStore : IDeployStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connections;

    public SqliteDeployStore(IDbConnectionFactory connections, DeploySchemaInitializer schema)
    {
        _connections = connections;
        schema.EnsureCreated();
    }

    public IReadOnlyList<DeploymentEnvironment> ListEnvironments(Guid? projectId = null)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        if (projectId is null)
        {
            command.CommandText = "SELECT payload FROM deploy_environments ORDER BY name";
        }
        else
        {
            command.CommandText = "SELECT payload FROM deploy_environments WHERE project_id=$project ORDER BY name";
            Add(command, "$project", projectId.Value.ToString());
        }

        return ReadAll<DeploymentEnvironment>(command);
    }

    public DeploymentEnvironment? FindEnvironment(Guid id)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM deploy_environments WHERE id=$id";
        Add(command, "$id", id.ToString());
        return ReadOne<DeploymentEnvironment>(command);
    }

    public void SaveEnvironment(DeploymentEnvironment environment)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO deploy_environments(id, project_id, name, payload, created_at)
            VALUES($id, $project, $name, $payload, $created)
            ON CONFLICT(id) DO UPDATE SET
                project_id=excluded.project_id,
                name=excluded.name,
                payload=excluded.payload
            """;
        Add(command, "$id", environment.Id.ToString());
        Add(command, "$project", environment.ProjectId.ToString());
        Add(command, "$name", environment.Name);
        Add(command, "$payload", JsonSerializer.Serialize(environment, Json));
        Add(command, "$created", environment.CreatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void DeleteEnvironment(Guid id)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM deploy_environments WHERE id=$id";
        Add(command, "$id", id.ToString());
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Deployment> ListDeployments(Guid? projectId = null, Guid? environmentId = null, int take = 100)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        if (environmentId is not null)
        {
            command.CommandText = "SELECT payload FROM deploy_deployments WHERE environment_id=$env ORDER BY created_at DESC LIMIT $take";
            Add(command, "$env", environmentId.Value.ToString());
        }
        else if (projectId is not null)
        {
            command.CommandText = "SELECT payload FROM deploy_deployments WHERE project_id=$project ORDER BY created_at DESC LIMIT $take";
            Add(command, "$project", projectId.Value.ToString());
        }
        else
        {
            command.CommandText = "SELECT payload FROM deploy_deployments ORDER BY created_at DESC LIMIT $take";
        }

        Add(command, "$take", take.ToString());
        return ReadAll<Deployment>(command);
    }

    public Deployment? FindDeployment(Guid id)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM deploy_deployments WHERE id=$id";
        Add(command, "$id", id.ToString());
        return ReadOne<Deployment>(command);
    }

    public void SaveDeployment(Deployment deployment)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO deploy_deployments(id, project_id, environment_id, status, payload, created_at)
            VALUES($id, $project, $env, $status, $payload, $created)
            ON CONFLICT(id) DO UPDATE SET
                project_id=excluded.project_id,
                environment_id=excluded.environment_id,
                status=excluded.status,
                payload=excluded.payload
            """;
        Add(command, "$id", deployment.Id.ToString());
        Add(command, "$project", deployment.ProjectId.ToString());
        Add(command, "$env", deployment.EnvironmentId.ToString());
        Add(command, "$status", deployment.Status.ToString());
        Add(command, "$payload", JsonSerializer.Serialize(deployment, Json));
        Add(command, "$created", deployment.CreatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static List<T> ReadAll<T>(DbCommand command)
    {
        using var reader = command.ExecuteReader();
        var result = new List<T>();
        while (reader.Read())
        {
            result.Add(Deserialize<T>(reader.GetString(0)));
        }

        return result;
    }

    private static T? ReadOne<T>(DbCommand command) where T : class
    {
        using var reader = command.ExecuteReader();
        return reader.Read() ? Deserialize<T>(reader.GetString(0)) : null;
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidOperationException("Corrupt deploy payload.");

    private static void Add(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
