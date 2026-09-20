using Modules.Review.Domain;
using Platform.Contracts.Events;
using Platform.Contracts.SourceControl;
using Platform.Core.Context;
using Platform.Core.SourceControl;

namespace Modules.Review.Application;

public sealed class ReviewService(
    IChangeRepository repository,
    SourceProviderRegistry providers,
    SourceRepositoryService sources,
    PlatformContextStore context,
    IEventPublisher events)
{
    public IReadOnlyList<Change> List(string? status = null, string? author = null, string? reviewer = null) => repository.List()
        .Where(change => status is null || change.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
        .Where(change => author is null || change.Author.Equals(author, StringComparison.OrdinalIgnoreCase))
        .Where(change => reviewer is null || change.Reviewers.Any(value => value.Name.Equals(reviewer, StringComparison.OrdinalIgnoreCase))).ToArray();
    public Change? Find(Guid id) => repository.Find(id);

    public async Task<Change?> GetAndRefreshAsync(Guid id, CancellationToken token)
    {
        var change = repository.Find(id); if (change is null) return null;
        return await RefreshAsync(change, token);
    }

    public async Task<Change> ImportAsync(Guid connectionId, string externalId, CancellationToken token = default)
    {
        var connection = sources.Find(connectionId) ?? throw new KeyNotFoundException("Source repository connection was not found.");
        var existing = repository.Find(connection.RepositoryId.Owner, connection.RepositoryId.Name, externalId);
        if (existing is not null) return await RefreshAsync(existing, token);
        var sourceRepository = await providers.Source(connection.RepositoryId.Provider).GetRepositoryAsync(connection.RepositoryId, token);
        var external = await providers.Changes(connection.RepositoryId.Provider).GetChangeAsync(connection.RepositoryId, externalId, token);
        var change = Change.FromExternal(context.Project.Id, sourceRepository, external);
        repository.Add(change);
        await events.PublishAsync(new ChangeOpened(change.Id, context.Project.Key, $"{sourceRepository.Owner}/{sourceRepository.Name}", change.SourceBranch, change.TargetBranch, change.HeadCommit), token);
        return change;
    }

    public async Task<Change> CreateAsync(Guid connectionId, CreateExternalChange request, CancellationToken token = default)
    {
        var connection = sources.Find(connectionId) ?? throw new KeyNotFoundException("Source repository connection was not found.");
        var external = await providers.Changes(connection.RepositoryId.Provider).CreateChangeAsync(connection.RepositoryId, request, token);
        return await ImportAsync(connectionId, external.ExternalId, token);
    }

    public async Task<SourceRepository> ConnectAsync(string providerId, string repositoryUrl, CancellationToken token = default)
    {
        var connection = await sources.ConnectAsync(providerId, repositoryUrl, token);
        return await providers.Source(providerId).GetRepositoryAsync(connection.RepositoryId, token);
    }

    public async Task<Change> ImportAsync(string providerId, string repositoryUrl, string externalId, CancellationToken token = default)
    {
        var connection = await sources.ConnectAsync(providerId, repositoryUrl, token);
        return await ImportAsync(connection.Id, externalId, token);
    }

    public Change? AddComment(Guid id, string author, string body, string? file, string? side, int? line, string? commitSha, Guid? parentId) =>
        Mutate(id, change => change.AddComment(author, body, file, side, line, commitSha, parentId));
    public Change? SetDiscussionResolution(Guid id, Guid discussionId, string actor, bool resolved) => Mutate(id, change => change.SetDiscussionResolution(discussionId, actor, resolved));
    public Change? AddReviewer(Guid id, string actor, string name) => Mutate(id, change => change.AddReviewer(actor, name));
    public Change? RemoveReviewer(Guid id, string actor, string name) => Mutate(id, change => change.RemoveReviewer(actor, name));
    public Change? SubmitReview(Guid id, string reviewer, string state, string? body) => Mutate(id, change => change.SubmitReview(reviewer, state, body));
    public Change? Approve(Guid id, string reviewer) => SubmitReview(id, reviewer, "Approved", null);
    public Change? RequestChanges(Guid id, string reviewer, string body) => SubmitReview(id, reviewer, "ChangesRequested", body);

    public async Task<Change?> MergeAsync(Guid id, string actor, CancellationToken token = default)
    {
        var change = repository.Find(id); if (change is null) return null;
        if (!change.CanMerge) throw new InvalidOperationException("One approval, no active changes-requested review, and a mergeable source branch are required.");
        var result = await providers.Changes(change.Repository.Provider).MergeChangeAsync(change.Repository.Id, new(change.ExternalId), token);
        if (!result.Merged) throw new SourceProviderException(SourceProviderErrorKind.Conflict, result.Message);
        change.MarkMerged(actor, result.CommitSha); repository.Update(change); return change;
    }

    public async Task<Change?> CloseAsync(Guid id, string actor, CancellationToken token = default)
    {
        var change = repository.Find(id); if (change is null) return null;
        await providers.Changes(change.Repository.Provider).CloseChangeAsync(change.Repository.Id, change.ExternalId, token);
        change.MarkClosed(actor); repository.Update(change); return change;
    }

    private async Task<Change> RefreshAsync(Change change, CancellationToken token)
    {
        var external = await providers.Changes(change.Repository.Provider).GetChangeAsync(change.Repository.Id, change.ExternalId, token);
        change.Synchronize(external); repository.Update(change); return change;
    }

    private Change? Mutate(Guid id, Action<Change> mutation)
    {
        var change = repository.Find(id); if (change is null) return null;
        mutation(change); repository.Update(change); return change;
    }
}
