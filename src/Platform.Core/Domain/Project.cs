namespace Platform.Core.Domain;

public sealed record Project(Guid Id, Guid OrganisationId, string Name, string Key, string? Repository);
