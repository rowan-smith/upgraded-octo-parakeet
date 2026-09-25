namespace ForgeDeck.Contracts.Capabilities;

public sealed class LicenceRequiredException(string capability)
    : InvalidOperationException($"Licence required for capability '{capability}'.")
{
    public string Capability { get; } = capability;
}
