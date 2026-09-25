namespace ForgeDeck.Git.Api;

public sealed record CreateRepositoryRequest(string Name);
public sealed record PushRequest(string Branch, string Message);
