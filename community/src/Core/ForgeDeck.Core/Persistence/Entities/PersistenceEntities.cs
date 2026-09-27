using ForgeDeck.Core.Domain;

namespace ForgeDeck.Core.Persistence.Entities;

public sealed class InstanceRow
{
    public int Singleton { get; set; } = 1;
    public InstanceState State { get; set; } = InstanceState.Uninitialised;
    public DateTimeOffset? InitialisedAt { get; set; }
    public Guid? InstanceId { get; set; }
    public LicenceMode LicenceMode { get; set; } = LicenceMode.None;
    public bool BootstrapEnabled { get; set; } = true;
    public DateTimeOffset? SetupCompletedAt { get; set; }
    public DateTimeOffset? ModulesAcknowledgedAt { get; set; }
    public DateTimeOffset? MembersAcknowledgedAt { get; set; }
    public DateTimeOffset? ProjectAcknowledgedAt { get; set; }
}

public sealed class UserProjectStar
{
    public Guid UserId { get; set; }
    public Guid ProjectId { get; set; }
    public DateTimeOffset StarredAt { get; set; }
}

public sealed class AuditEventRow
{
    public Guid Id { get; set; }
    public string Actor { get; set; } = "";
    public string Organisation { get; set; } = "";
    public string Project { get; set; } = "";
    public string Module { get; set; } = "";
    public string Action { get; set; } = "";
    public string Resource { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public string CorrelationId { get; set; } = "";
    public string? MetadataJson { get; set; }
}

public sealed class ProviderCredentialRow
{
    public string ProviderId { get; set; } = "";
    public string ProtectedSecret { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SourceConnectionRow
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string ProviderId { get; set; } = "";
    public string Owner { get; set; } = "";
    public string RepositoryName { get; set; } = "";
    public string DefaultBranch { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTimeOffset ConnectedAt { get; set; }
}

public sealed class LocalRepositoryRow
{
    public Guid ProjectId { get; set; }
    public string Path { get; set; } = "";
    public string Root { get; set; } = "";
    public DateTimeOffset AssociatedAt { get; set; }
}
