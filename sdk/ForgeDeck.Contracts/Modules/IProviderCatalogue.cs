namespace ForgeDeck.Contracts.Modules;

public enum ProviderKind
{
    Source,
    ChangeSource,
    Check,
    BuildExecution,
    Artifact,
    Secret,
    Deployment,
    Identity
}

public sealed record ProviderRegistration(
    string ExtensionId,
    string ProviderId,
    ProviderKind Kind,
    string ContractName);

/// <summary>
/// Catalogue of installed providers. Prefer "does something provide Check?" over "is Build installed?".
/// </summary>
public interface IProviderCatalogue
{
    IReadOnlyList<ProviderRegistration> All { get; }
    IReadOnlyList<ProviderRegistration> OfKind(ProviderKind kind);
    bool Has(ProviderKind kind);
}
