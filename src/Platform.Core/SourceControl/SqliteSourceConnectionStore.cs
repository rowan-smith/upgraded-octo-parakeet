using Platform.Contracts.SourceControl;
using Platform.Core.Persistence;

namespace Platform.Core.SourceControl;

public sealed class SqliteSourceConnectionStore : ISourceConnectionStore
{
    private readonly IDbConnectionFactory _connections;
    public SqliteSourceConnectionStore(IDbConnectionFactory connections, CoreSchemaInitializer schema) { _connections = connections; schema.EnsureCreated(); }

    public IReadOnlyList<SourceRepositoryConnection> List(Guid projectId)
    {
        using var connection = _connections.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, project_id, provider_id, owner, repository_name, default_branch, url, connected_at FROM core_source_connections WHERE project_id = $project";
        Add(command, "$project", projectId.ToString()); using var reader = command.ExecuteReader(); var result = new List<SourceRepositoryConnection>();
        while (reader.Read()) result.Add(Map(reader)); return result;
    }

    public SourceRepositoryConnection? Find(Guid id) => FindBy("id", id.ToString());
    public SourceRepositoryConnection? Find(Guid projectId, RepositoryId repositoryId)
    {
        using var connection = _connections.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, project_id, provider_id, owner, repository_name, default_branch, url, connected_at FROM core_source_connections WHERE project_id=$project AND provider_id=$provider AND owner=$owner AND repository_name=$name";
        Add(command, "$project", projectId.ToString()); Add(command, "$provider", repositoryId.Provider); Add(command, "$owner", repositoryId.Owner); Add(command, "$name", repositoryId.Name);
        using var reader = command.ExecuteReader(); return reader.Read() ? Map(reader) : null;
    }

    public void Save(SourceRepositoryConnection value)
    {
        using var connection = _connections.Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO core_source_connections(id, project_id, provider_id, owner, repository_name, default_branch, url, connected_at)
            VALUES($id,$project,$provider,$owner,$name,$branch,$url,$connected)
            ON CONFLICT(project_id, provider_id, owner, repository_name) DO UPDATE SET default_branch=excluded.default_branch,url=excluded.url
            """;
        Add(command, "$id", value.Id.ToString()); Add(command, "$project", value.ProjectId.ToString()); Add(command, "$provider", value.RepositoryId.Provider);
        Add(command, "$owner", value.RepositoryId.Owner); Add(command, "$name", value.RepositoryId.Name); Add(command, "$branch", value.DefaultBranch);
        Add(command, "$url", value.Url); Add(command, "$connected", value.ConnectedAt.ToString("O")); command.ExecuteNonQuery();
    }

    private SourceRepositoryConnection? FindBy(string column, string value)
    {
        using var connection = _connections.Open(); using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id, project_id, provider_id, owner, repository_name, default_branch, url, connected_at FROM core_source_connections WHERE {column}=$value";
        Add(command, "$value", value); using var reader = command.ExecuteReader(); return reader.Read() ? Map(reader) : null;
    }
    private static SourceRepositoryConnection Map(System.Data.Common.DbDataReader reader) => new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
        new(reader.GetString(2), reader.GetString(3), reader.GetString(4)), reader.GetString(5), reader.GetString(6), DateTimeOffset.Parse(reader.GetString(7)));
    private static void Add(System.Data.Common.DbCommand command, string name, object value) { var parameter=command.CreateParameter(); parameter.ParameterName=name; parameter.Value=value; command.Parameters.Add(parameter); }
}
