using ForgeDeck.Contracts.Services;

namespace ForgeDeck.Build.Application;

public sealed class BuildDomainService : IBuildService
{
    public string ServiceId => "build";
}
