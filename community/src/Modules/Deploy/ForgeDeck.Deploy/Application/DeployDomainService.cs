using ForgeDeck.Contracts.Services;

namespace ForgeDeck.Deploy.Application;

public sealed class DeployDomainService : IDeployService
{
    public string ServiceId => "deploy";
}
