namespace Platform.Contracts.Capabilities;

public interface ICapabilityService
{
    bool Has(string capability);
    IReadOnlySet<string> Current { get; }
}
