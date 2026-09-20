using Platform.Contracts.SourceControl;
using Platform.Core.Persistence;

namespace Platform.Core.SourceControl;

public sealed class SqliteLocalRepositoryStore : ILocalRepositoryStore
{
    private readonly IDbConnectionFactory _connections;
    public SqliteLocalRepositoryStore(IDbConnectionFactory connections, CoreSchemaInitializer schema)
    {
        _connections = connections;
        schema.EnsureCreated();
    }

    public LocalRepositoryAssociation? Find(Guid projectId)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT project_id, path, root, associated_at FROM core_local_repositories WHERE project_id=$project";
        Add(command, "$project", projectId.ToString());
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3)));
    }

    public void Save(LocalRepositoryAssociation association)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO core_local_repositories(project_id, path, root, associated_at)
            VALUES($project,$path,$root,$associated)
            ON CONFLICT(project_id) DO UPDATE SET path=excluded.path, root=excluded.root, associated_at=excluded.associated_at
            """;
        Add(command, "$project", association.ProjectId.ToString());
        Add(command, "$path", association.Path);
        Add(command, "$root", association.Root);
        Add(command, "$associated", association.AssociatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void Remove(Guid projectId)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM core_local_repositories WHERE project_id=$project";
        Add(command, "$project", projectId.ToString());
        command.ExecuteNonQuery();
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
