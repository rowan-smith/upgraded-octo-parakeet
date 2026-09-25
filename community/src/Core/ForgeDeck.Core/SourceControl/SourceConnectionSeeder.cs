using ForgeDeck.Contracts.SourceControl;
using ForgeDeck.Core.Context;
using Microsoft.Extensions.Configuration;

namespace ForgeDeck.Core.SourceControl;

public sealed class SourceConnectionSeeder(IConfiguration configuration, ISourceConnectionStore connections, PlatformContextStore context)
{
    public void Seed()
    {
        var section = configuration.GetSection("Source:DefaultRepository");
        var provider = section["Provider"]; var owner = section["Owner"]; var name = section["Name"];
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var repositoryId = new RepositoryId(provider, owner, name);
        if (connections.Find(context.Project.Id, repositoryId) is not null)
        {
            return;
        }

        connections.Save(new(Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"), context.Project.Id, repositoryId,
            section["DefaultBranch"] ?? "main", section["Url"] ?? $"https://github.com/{owner}/{name}", DateTimeOffset.UtcNow));
    }
}
