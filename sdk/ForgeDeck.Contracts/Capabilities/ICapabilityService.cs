namespace ForgeDeck.Contracts.Capabilities;

public interface ICapabilityService
{
    bool Has(string capability);
    bool Has(Guid organisationId, string capability);
    IReadOnlySet<string> ForOrganisation(Guid organisationId);
    IReadOnlySet<string> Current { get; }
    string EditionFor(Guid organisationId, string moduleId, string installedEdition);
}
