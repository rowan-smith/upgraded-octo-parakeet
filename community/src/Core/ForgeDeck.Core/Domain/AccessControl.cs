namespace ForgeDeck.Core.Domain;

/// <summary>Where a role, assignment, or permission grant applies.</summary>
public enum ScopeType
{
    Organisation,
    Team,
    Project
}

/// <summary>Who owns a custom role definition.</summary>
public enum RoleOwnerType
{
    Organisation,
    Team,
    Project
}

/// <summary>How a team relates to a project.</summary>
public enum ProjectTeamRelationship
{
    Owner,
    Access
}

/// <summary>Identifies the resource boundary for permission evaluation.</summary>
public readonly record struct ResourceScope(ScopeType Type, Guid? Id = null)
{
    public static ResourceScope Organisation { get; } = new(ScopeType.Organisation);

    public static ResourceScope ForTeam(Guid teamId) => new(ScopeType.Team, teamId);

    public static ResourceScope ForProject(Guid projectId) => new(ScopeType.Project, projectId);

    public override string ToString() => Id is Guid id ? $"{Type}:{id}" : Type.ToString();
}

/// <summary>A role assignment connecting a user to a role inside a scope.</summary>
public sealed class RoleAssignment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public Guid RoleId { get; init; }
    public ScopeType ScopeType { get; init; }
    public Guid? ScopeId { get; init; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset AssignedAt { get; init; } = DateTimeOffset.UtcNow;
    public Guid? AssignedByUserId { get; init; }

    public bool IsExpired(DateTimeOffset? now = null) =>
        ExpiresAt is DateTimeOffset expires && expires <= (now ?? DateTimeOffset.UtcNow);
}

/// <summary>Direct permission grant to a user without requiring a role.</summary>
public sealed class PermissionGrant
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public required string PermissionId { get; set; }
    public ScopeType ScopeType { get; init; }
    public Guid? ScopeId { get; init; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset GrantedAt { get; init; } = DateTimeOffset.UtcNow;
    public Guid? GrantedByUserId { get; init; }

    public bool IsExpired(DateTimeOffset? now = null) =>
        ExpiresAt is DateTimeOffset expires && expires <= (now ?? DateTimeOffset.UtcNow);
}

/// <summary>One contributing path that grants a permission.</summary>
public sealed record PermissionSource(
    string Kind,
    string? RoleSlug,
    string? RoleName,
    Guid? RoleId,
    Guid? TeamId,
    string? TeamName,
    Guid? ProjectId,
    string? ProjectName,
    Guid? GrantedByUserId,
    DateTimeOffset? GrantedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>Explains why a user holds (or lacks) a permission in a scope.</summary>
public sealed record PermissionExplanation(
    string PermissionId,
    bool Granted,
    ResourceScope Scope,
    IReadOnlyList<PermissionSource> Sources);

/// <summary>Resolved effective permissions plus provenance for each key.</summary>
public sealed class EffectivePermissionSet
{
    public required IReadOnlySet<string> Permissions { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<PermissionSource>> Sources { get; init; }

    public static EffectivePermissionSet Empty { get; } = new()
    {
        Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        Sources = new Dictionary<string, IReadOnlyList<PermissionSource>>(StringComparer.OrdinalIgnoreCase)
    };

    public bool Contains(string permission) => Permissions.Contains(permission);
}
