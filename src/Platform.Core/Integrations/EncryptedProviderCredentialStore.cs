using Microsoft.AspNetCore.DataProtection;
using Platform.Contracts.Integrations;
using Platform.Core.Persistence;

namespace Platform.Core.Integrations;

public sealed class EncryptedProviderCredentialStore : IProviderCredentialStore
{
    private readonly IDbConnectionFactory _connections;
    private readonly IDataProtector _protector;

    public EncryptedProviderCredentialStore(IDbConnectionFactory connections, CoreSchemaInitializer schema, IDataProtectionProvider protection)
    {
        _connections = connections;
        _protector = protection.CreateProtector("ForgeDeck.ProviderCredentials.v1");
        schema.EnsureCreated();
    }

    public string? Get(string providerId)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT protected_secret FROM core_integrations WHERE provider_id = $provider";
        Add(command, "$provider", providerId);
        var protectedSecret = command.ExecuteScalar() as string;
        return protectedSecret is null ? null : _protector.Unprotect(protectedSecret);
    }

    public bool IsConfigured(string providerId)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM core_integrations WHERE provider_id = $provider";
        Add(command, "$provider", providerId);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    public void Set(string providerId, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("A token is required.");
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO core_integrations(provider_id, protected_secret, updated_at) VALUES($provider, $secret, $updated)
            ON CONFLICT(provider_id) DO UPDATE SET protected_secret = excluded.protected_secret, updated_at = excluded.updated_at
            """;
        Add(command, "$provider", providerId);
        Add(command, "$secret", _protector.Protect(secret.Trim()));
        Add(command, "$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void Delete(string providerId)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM core_integrations WHERE provider_id = $provider";
        Add(command, "$provider", providerId);
        command.ExecuteNonQuery();
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
    }
}
