namespace Platform.Contracts.Integrations;

public interface IProviderCredentialStore
{
    string? Get(string providerId);
    bool IsConfigured(string providerId);
    void Set(string providerId, string secret);
    void Delete(string providerId);
}
