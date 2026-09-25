using System.Data.Common;
using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Core.Persistence;

namespace ForgeDeck.Core.Extensions;

public interface IExtensionRegistry
{
    IReadOnlyList<ExtensionInstallation> List();
    ExtensionInstallation? Find(string extensionId);
    bool IsEnabled(string extensionId);
    bool IsRuntimeEnabled(string runtimeId);
    void Save(ExtensionInstallation installation);
    void Delete(string extensionId);
    bool HasAnyInstalled();
}

public sealed class SqliteExtensionRegistry : IExtensionRegistry
{
    private readonly IDbConnectionFactory _connections;

    public SqliteExtensionRegistry(IDbConnectionFactory connections, CoreSchemaInitializer schema)
    {
        _connections = connections;
        schema.EnsureCreated();
    }

    public IReadOnlyList<ExtensionInstallation> List()
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT extension_id, type, runtime_id, installed_version, state, enabled, installed_at, installed_by, updated_at, last_error, restart_required FROM core_extensions ORDER BY extension_id";
        using var reader = command.ExecuteReader();
        var list = new List<ExtensionInstallation>();
        while (reader.Read())
        {
            list.Add(Map(reader));
        }

        return list;
    }

    public ExtensionInstallation? Find(string extensionId)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT extension_id, type, runtime_id, installed_version, state, enabled, installed_at, installed_by, updated_at, last_error, restart_required FROM core_extensions WHERE extension_id=$id";
        Add(command, "$id", extensionId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public bool IsEnabled(string extensionId)
    {
        var row = Find(extensionId);
        return row is { Enabled: true, State: ExtensionLifecycleState.Enabled or ExtensionLifecycleState.RestartRequired };
    }

    public bool IsRuntimeEnabled(string runtimeId)
    {
        var entry = BuiltinExtensionCatalogue.FindByRuntimeId(runtimeId);
        return entry is not null && IsEnabled(entry.ExtensionId);
    }

    public bool HasAnyInstalled() => List().Any(x => x.State is not ExtensionLifecycleState.Available);

    public void Save(ExtensionInstallation installation)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO core_extensions(extension_id, type, runtime_id, installed_version, state, enabled, installed_at, installed_by, updated_at, last_error, restart_required)
            VALUES($id, $type, $runtime, $version, $state, $enabled, $installedAt, $installedBy, $updated, $error, $restart)
            ON CONFLICT(extension_id) DO UPDATE SET
              type=excluded.type,
              runtime_id=excluded.runtime_id,
              installed_version=excluded.installed_version,
              state=excluded.state,
              enabled=excluded.enabled,
              installed_at=excluded.installed_at,
              installed_by=excluded.installed_by,
              updated_at=excluded.updated_at,
              last_error=excluded.last_error,
              restart_required=excluded.restart_required
            """;
        Add(command, "$id", installation.ExtensionId);
        Add(command, "$type", installation.Type.ToString());
        Add(command, "$runtime", installation.RuntimeId);
        Add(command, "$version", installation.InstalledVersion);
        Add(command, "$state", installation.State.ToString());
        Add(command, "$enabled", installation.Enabled ? 1 : 0);
        Add(command, "$installedAt", installation.InstalledAt?.ToString("O"));
        Add(command, "$installedBy", installation.InstalledBy);
        Add(command, "$updated", installation.UpdatedAt.ToString("O"));
        Add(command, "$error", installation.LastError);
        Add(command, "$restart", installation.RestartRequired ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public void Delete(string extensionId)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM core_extensions WHERE extension_id=$id";
        Add(command, "$id", extensionId);
        command.ExecuteNonQuery();
    }

    private static ExtensionInstallation Map(DbDataReader reader) => new()
    {
        ExtensionId = reader.GetString(0),
        Type = Enum.Parse<ExtensionType>(reader.GetString(1)),
        RuntimeId = reader.IsDBNull(2) ? null : reader.GetString(2),
        InstalledVersion = reader.IsDBNull(3) ? null : reader.GetString(3),
        State = Enum.Parse<ExtensionLifecycleState>(reader.GetString(4)),
        Enabled = reader.GetInt64(5) != 0,
        InstalledAt = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
        InstalledBy = reader.IsDBNull(7) ? null : reader.GetString(7),
        UpdatedAt = DateTimeOffset.Parse(reader.GetString(8)),
        LastError = reader.IsDBNull(9) ? null : reader.GetString(9),
        RestartRequired = reader.GetInt64(10) != 0
    };

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
