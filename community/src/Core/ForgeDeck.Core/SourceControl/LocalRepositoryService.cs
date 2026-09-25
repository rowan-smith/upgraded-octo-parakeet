using ForgeDeck.Contracts.SourceControl;
using ForgeDeck.Core.Context;

namespace ForgeDeck.Core.SourceControl;

public sealed class LocalRepositoryService(
    ILocalRepositoryStore store,
    LocalRepositoryInspector inspector,
    PlatformContextStore context,
    SourceRepositoryService sources)
{
    public LocalRepositoryAssociation? CurrentAssociation() => store.Find(context.Project.Id);

    public LocalRepositoryInfo? CurrentStatus()
    {
        var association = store.Find(context.Project.Id);
        return association is null ? null : inspector.Inspect(association.Root);
    }

    public LocalRepositoryInfo Detect(string path) => inspector.Inspect(path);

    public async Task<(LocalRepositoryAssociation Association, LocalRepositoryInfo Info, SourceRepositoryConnection? Connected)> AssociateAsync(
        string path, bool connectGitHub, CancellationToken token)
    {
        var info = inspector.Inspect(path);
        if (!info.IsGitRepository)
        {
            throw new InvalidOperationException("The selected path is not a Git repository.");
        }

        var association = new LocalRepositoryAssociation(context.Project.Id, info.Path, info.Root, DateTimeOffset.UtcNow);
        store.Save(association);
        SourceRepositoryConnection? connection = null;
        if (connectGitHub && info.DetectedGitHub is { } github)
        {
            connection = await sources.ConnectAsync("github", github.Url, token);
        }

        return (association, info, connection);
    }

    public void Disconnect() => store.Remove(context.Project.Id);
}
