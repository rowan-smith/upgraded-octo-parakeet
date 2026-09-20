using Modules.Review.Domain;
using Platform.Contracts.Checks;
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
    IEventPublisher events,
    Microsoft.Extensions.Options.IOptions<ReviewOptions> options,
    ApprovalPolicyResolver policies,
    CheckQueryService checks,
    IEnumerable<ICheckProvider> checkProviders)
{
    private readonly bool _hasCheckProviders = checkProviders.Any();
    public IReadOnlyList<Change> List(string? status = null, string? author = null, string? reviewer = null) => repository.List()
        .Where(change => status is null || change.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
        .Where(change => author is null || change.Author.Equals(author, StringComparison.OrdinalIgnoreCase))
        .Where(change => reviewer is null || change.Reviewers.Any(value => value.Name.Equals(reviewer, StringComparison.OrdinalIgnoreCase))).ToArray();
    public Change? Find(Guid id) => repository.Find(id);
    public bool AllowSelfReview => options.Value.AllowSelfReview;
    public ReviewPolicySnapshot Policy => policies.Snapshot();

    public async Task<Change?> GetAndRefreshAsync(Guid id, CancellationToken token)
    {
        var change = repository.Find(id); if (change is null) return null;
        return await RefreshAsync(change, token);
    }

    public async Task<object?> GetResponseAsync(Guid id, bool refresh, CancellationToken token)
    {
        var change = refresh ? await GetAndRefreshAsync(id, token) : Find(id);
        return change is null ? null : await ToResponseAsync(change, token);
    }

    public async Task<object> ToResponseAsync(Change change, CancellationToken token = default) =>
        ChangeResponse.Create(change, await EvaluateMergeAsync(change, token));

    public async Task<ApprovalPolicyResult> EvaluateMergeAsync(Change change, CancellationToken token = default)
    {
        var approval = change.EvaluatePolicy(policies.Resolve());
        // RequiredChecks in base appsettings must not block Review-only hosts with no check providers.
        var required = _hasCheckProviders ? options.Value.RequiredChecks : [];
        if (required.Count == 0)
            return new(approval.HasApproval, approval.HasBlockingReview, approval.ProviderMergeable, RequiredChecks: null);

        var results = await checks.GetAsync(change.Id, token);
        var statuses = RequiredCheckEvaluator.EvaluateAll(results, change.HeadCommit, required)
            .Select(item => new CheckRequirementStatus(item.Name, item.Satisfied, item.Status))
            .ToArray();
        return new(approval.HasApproval, approval.HasBlockingReview, approval.ProviderMergeable, statuses);
    }

    public async Task<IReadOnlyList<ExternalChange>> DiscoverAsync(Guid connectionId, CancellationToken token = default)
    {
        var connection = sources.Find(connectionId) ?? throw new KeyNotFoundException("Source repository connection was not found.");
        return await providers.Changes(connection.RepositoryId.Provider).GetChangesAsync(connection.RepositoryId, token);
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
        await events.PublishAsync(new ChangeOpened(
            change.Id,
            context.Project.Key,
            $"{sourceRepository.Owner}/{sourceRepository.Name}",
            change.SourceBranch,
            change.TargetBranch,
            change.HeadCommit,
            ResolveCloneUrl(sourceRepository)), token);
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
    public Change? EditComment(Guid id, Guid commentId, string actor, string body) => Mutate(id, change => change.EditComment(commentId, actor, body));
    public Change? DeleteComment(Guid id, Guid commentId, string actor) => Mutate(id, change => change.DeleteComment(commentId, actor));
    public Change? SetDiscussionResolution(Guid id, Guid discussionId, string actor, bool resolved) => Mutate(id, change => change.SetDiscussionResolution(discussionId, actor, resolved));
    public Change? AddReviewer(Guid id, string actor, string name) => Mutate(id, change => change.AddReviewer(actor, name));
    public Change? RemoveReviewer(Guid id, string actor, string name) => Mutate(id, change => change.RemoveReviewer(actor, name));
    public Change? SubmitReview(Guid id, string reviewer, string state, string? body)
    {
        var change = repository.Find(id); if (change is null) return null;
        if (!AllowSelfReview && change.Author.Equals(reviewer, StringComparison.OrdinalIgnoreCase) && state is "Approved" or "ChangesRequested")
            throw new InvalidOperationException("Self-review is disabled for this project.");
        change.SubmitReview(reviewer, state, body, policies.Resolve()); repository.Update(change); return change;
    }
    public Change? Approve(Guid id, string reviewer) => SubmitReview(id, reviewer, "Approved", null);
    public Change? RequestChanges(Guid id, string reviewer, string body) => SubmitReview(id, reviewer, "ChangesRequested", body);

    public async Task<SourceDiff?> GetFileDiffAsync(Guid id, string path, CancellationToken token = default)
    {
        var change = repository.Find(id); if (change is null) return null;
        var file = change.Files.FirstOrDefault(value => value.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (file is not null) return new SourceDiff(change.BaseCommit, change.HeadCommit, [file]);
        var diff = await providers.Changes(change.Repository.Provider).GetChangeDiffAsync(change.Repository.Id, change.ExternalId, token);
        file = diff.Files.FirstOrDefault(value => value.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"File '{path}' was not found in the change diff.");
        return new SourceDiff(diff.BaseSha, diff.HeadSha, [file]);
    }

    public async Task<Change?> MergeAsync(Guid id, string actor, CancellationToken token = default)
    {
        var change = repository.Find(id); if (change is null) return null;
        var mergePolicy = await EvaluateMergeAsync(change, token);
        if (!mergePolicy.Satisfied) throw new InvalidOperationException("Approval policy is not satisfied for merge.");
        var policy = policies.Resolve();
        var result = await providers.Changes(change.Repository.Provider).MergeChangeAsync(change.Repository.Id, new(change.ExternalId), token);
        if (!result.Merged) throw new SourceProviderException(SourceProviderErrorKind.Conflict, result.Message);
        change.MarkMerged(actor, result.CommitSha, policy); repository.Update(change); return change;
    }

    public async Task<Change?> CloseAsync(Guid id, string actor, CancellationToken token = default)
    {
        var change = repository.Find(id); if (change is null) return null;
        await providers.Changes(change.Repository.Provider).CloseChangeAsync(change.Repository.Id, change.ExternalId, token);
        change.MarkClosed(actor); repository.Update(change); return change;
    }

    private async Task<Change> RefreshAsync(Change change, CancellationToken token)
    {
        var previousCommitSha = change.HeadCommit;
        var external = await providers.Changes(change.Repository.Provider).GetChangeAsync(change.Repository.Id, change.ExternalId, token);
        change.Synchronize(external); repository.Update(change);
        if (!string.Equals(previousCommitSha, change.HeadCommit, StringComparison.OrdinalIgnoreCase))
        {
            await events.PublishAsync(new ChangeUpdated(
                change.Id,
                context.Project.Key,
                $"{change.Repository.Owner}/{change.Repository.Name}",
                change.SourceBranch,
                change.TargetBranch,
                change.HeadCommit,
                previousCommitSha,
                ResolveCloneUrl(change.Repository)), token);
        }
        return change;
    }

    private static string? ResolveCloneUrl(SourceRepository repository)
    {
        if (!string.IsNullOrWhiteSpace(repository.Url)) return repository.Url;
        if (repository.Provider.Equals("github", StringComparison.OrdinalIgnoreCase))
            return $"https://github.com/{repository.Owner}/{repository.Name}.git";
        return null;
    }

    private Change? Mutate(Guid id, Action<Change> mutation)
    {
        var change = repository.Find(id); if (change is null) return null;
        mutation(change); repository.Update(change); return change;
    }
}
