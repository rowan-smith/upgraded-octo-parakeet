using System.Text.Json;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Review.Application;
using ForgeDeck.Review.Domain;

namespace ForgeDeck.Review.Infrastructure;

public sealed class SqliteChangeRepository : IChangeRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connections;
    public SqliteChangeRepository(IDbConnectionFactory connections, ReviewSchemaInitializer schema) { _connections = connections; schema.EnsureCreated(); }

    public IReadOnlyList<Change> List()
    {
        using var connection = _connections.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM review_changes ORDER BY updated_at DESC";
        using var reader = command.ExecuteReader(); var result = new List<Change>();
        while (reader.Read())
        {
            result.Add(Deserialize(reader.GetString(0)));
        }

        return result;
    }

    public Change? Find(Guid id) => FindBy("id", id.ToString());
    public Change? Find(string owner, string repository, string externalId)
    {
        using var connection = _connections.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM review_changes WHERE owner=$owner AND repository_name=$repository AND external_id=$external";
        Add(command, "$owner", owner); Add(command, "$repository", repository); Add(command, "$external", externalId);
        return command.ExecuteScalar() is string payload ? Deserialize(payload) : null;
    }

    public void Add(Change change) => Save(change);
    public void Update(Change change) => Save(change);

    private void Save(Change change)
    {
        using var connection = _connections.Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO review_changes(id,project_id,provider_id,owner,repository_name,external_id,status,updated_at,payload)
            VALUES($id,$project,$provider,$owner,$repository,$external,$status,$updated,$payload)
            ON CONFLICT(id) DO UPDATE SET status=excluded.status,updated_at=excluded.updated_at,payload=excluded.payload
            """;
        Add(command, "$id", change.Id.ToString()); Add(command, "$project", change.ProjectId.ToString()); Add(command, "$provider", change.Repository.Provider);
        Add(command, "$owner", change.Repository.Owner); Add(command, "$repository", change.Repository.Name); Add(command, "$external", change.ExternalId);
        Add(command, "$status", change.Status); Add(command, "$updated", change.UpdatedAt.ToString("O")); Add(command, "$payload", JsonSerializer.Serialize(change, Json)); command.ExecuteNonQuery();
    }

    private Change? FindBy(string column, string value)
    {
        using var connection = _connections.Open(); using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM review_changes WHERE {column}=$value"; Add(command, "$value", value);
        return command.ExecuteScalar() is string payload ? Deserialize(payload) : null;
    }
    private static Change Deserialize(string payload) => JsonSerializer.Deserialize<Change>(payload, Json) ?? throw new InvalidOperationException("Stored change is invalid.");
    private static void Add(System.Data.Common.DbCommand command, string name, object value) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
}
