namespace Modules.Review.Api;

public sealed record ImportRequest(Guid? RepositoryConnectionId, string ExternalId, string? RepositoryUrl = null, string ProviderId = "github");
public sealed record CreateChangeRequest(Guid RepositoryConnectionId, string SourceBranch, string TargetBranch, string Title, string Description);
public sealed record CommentRequest(string Body, string? File = null, string? Side = null, int? Line = null, string? CommitSha = null, Guid? ParentId = null);
public sealed record ReviewerRequest(string Name);
public sealed record ReviewRequest(string State, string? Body = null);
