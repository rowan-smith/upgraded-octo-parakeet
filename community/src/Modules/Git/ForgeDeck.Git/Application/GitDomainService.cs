using ForgeDeck.Contracts.Services;

namespace ForgeDeck.Git.Application;

/// <summary>In-process adapter. Remoting can replace this without changing Review/Build callers.</summary>
public sealed class GitDomainService : IGitService
{
    public string ServiceId => "git";
}
