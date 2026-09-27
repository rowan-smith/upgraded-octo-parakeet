using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ForgeDeck.Core.Persistence;

/// <summary>
/// Shared relational provider selection for Core and Module DbContexts.
/// SQLite for development/tests; PostgreSQL recommended for production.
/// </summary>
public static class DatabaseProvider
{
    public const string Sqlite = "Sqlite";
    public const string PostgreSql = "PostgreSql";

    public static string Resolve(IConfiguration configuration)
    {
        var configured = configuration["Database:Provider"]
            ?? configuration["Database.Provider"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Sqlite;
        }

        return configured.Trim() switch
        {
            var v when v.Equals("Postgres", StringComparison.OrdinalIgnoreCase) => PostgreSql,
            var v when v.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) => PostgreSql,
            var v when v.Equals(PostgreSql, StringComparison.OrdinalIgnoreCase) => PostgreSql,
            _ => Sqlite
        };
    }

    public static bool IsPostgreSql(IConfiguration configuration) =>
        Resolve(configuration).Equals(PostgreSql, StringComparison.OrdinalIgnoreCase);

    public static void Configure(DbContextOptionsBuilder options, IConfiguration configuration, string? connectionString)
    {
        var cs = string.IsNullOrWhiteSpace(connectionString)
            ? configuration.GetConnectionString("Platform") ?? "Data Source=data/forgedeck.db"
            : connectionString;

        if (IsPostgreSql(configuration))
        {
            options.UseNpgsql(cs);
            return;
        }

        options.UseSqlite(cs);
    }
}
