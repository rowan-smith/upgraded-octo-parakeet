using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace ForgeDeck.Core.Persistence;

public sealed class SqliteConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public DbConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var path = builder.DataSource;
        if (!string.IsNullOrWhiteSpace(path) && path != ":memory:")
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }
        }
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
