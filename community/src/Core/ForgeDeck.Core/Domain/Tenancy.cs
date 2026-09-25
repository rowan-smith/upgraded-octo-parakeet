namespace ForgeDeck.Core.Domain;

public enum UserStatus { Active, Suspended, Disabled }
public enum MembershipStatus { Invited, Active, Suspended }
public enum OrganisationRole { Owner, Admin, Member }
public enum ProjectVisibility { Private, Organisation }
public enum RepositoryStatus { Connected, Unavailable, AuthenticationRequired, Disconnected }
public enum InstanceState { Uninitialised, Initialised }
public enum LicenceMode { None, Community, Commercial }
public enum RepositoryMode { SingleRepository, MultiRepository }
public enum LicenceRecordStatus { Active, Replaced, Removed, Expired }

/// <summary>Stable IDs preserved for existing dogfood data / licences.</summary>
public static class KnownIds
{
    public static readonly Guid OrganisationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid AtlasProjectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid MayaUserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
}

// Compatibility projections used by PlatformContextStore and feature modules.
public sealed record OrganisationView(Guid Id, string Name, string Slug);
public sealed record ProjectView(Guid Id, Guid OrganisationId, string Name, string Key, string? Repository);

public sealed class InstanceConfiguration
{
    public Guid InstanceId { get; set; }
    public InstanceState State { get; set; } = InstanceState.Uninitialised;
    public LicenceMode LicenceMode { get; set; } = LicenceMode.None;
    public bool BootstrapEnabled { get; set; } = true;
    public DateTimeOffset? InitialisedAt { get; set; }
    public DateTimeOffset? SetupCompletedAt { get; set; }
    /// <summary>Set when the Modules setup step is completed or skipped.</summary>
    public DateTimeOffset? ModulesAcknowledgedAt { get; set; }
}

public sealed class ProjectModuleSetting
{
    public Guid ProjectId { get; init; }
    public required string ExtensionId { get; init; }
    public bool Enabled { get; set; } = true;
}

public sealed class BootstrapSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string TokenHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class LicenceRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? LicenceId { get; set; }
    public string? CustomerId { get; set; }
    public LicenceMode Mode { get; set; }
    public LicenceRecordStatus Status { get; set; } = LicenceRecordStatus.Active;
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Payload { get; set; }
    public string? Signature { get; set; }
    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class LicenceHistoryEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Action { get; init; }
    public LicenceMode? Mode { get; init; }
    public string? LicenceId { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Exactly one row per installation.</summary>
public sealed class Organisation
{
    public Guid Id { get; init; } = KnownIds.OrganisationId;
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserAccount
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? OnboardingDismissedAt { get; set; }
}

public sealed class UserProfile
{
    public Guid UserId { get; init; }
    public required string DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public string? JobTitle { get; set; }
    public string? Timezone { get; set; }
    public string? Locale { get; set; }
    public Guid? DefaultProjectId { get; set; }
    /// <summary>Preferred UI theme: light, dark, or system.</summary>
    public string Theme { get; set; } = "system";
}

public sealed class OrganisationMembership
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public OrganisationRole Role { get; set; } = OrganisationRole.Member;
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    public DateTimeOffset JoinedAt { get; init; } = DateTimeOffset.UtcNow;
    public Guid? InvitedByUserId { get; init; }
}

public sealed class Invitation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Email { get; set; }
    public OrganisationRole Role { get; set; } = OrganisationRole.Member;
    public required string TokenHash { get; init; }
    public Guid InvitedByUserId { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class Team
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TeamMembership
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TeamId { get; init; }
    public Guid UserId { get; init; }
    public DateTimeOffset JoinedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class Project
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    /// <summary>Short key for events (e.g. ATL).</summary>
    public required string Key { get; set; }
    public string? Description { get; set; }
    public ProjectVisibility Visibility { get; set; } = ProjectVisibility.Private;
    public RepositoryMode RepositoryMode { get; set; } = RepositoryMode.SingleRepository;
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class ProjectUserAccess
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public Guid UserId { get; init; }
    public DateTimeOffset GrantedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ProjectTeamAccess
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public Guid TeamId { get; init; }
    public DateTimeOffset GrantedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class Repository
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? DefaultBranch { get; set; }
    public RepositoryStatus Status { get; set; } = RepositoryStatus.Connected;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class RepositoryConnection
{
    public Guid RepositoryId { get; init; }
    public required string ProviderType { get; set; }
    public string? ExternalRepositoryId { get; set; }
    public required string ExternalOwner { get; set; }
    public required string ExternalName { get; set; }
    public required string CloneUrl { get; set; }
    public string? WebUrl { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public RepositoryStatus Status { get; set; } = RepositoryStatus.Connected;
}

public sealed class UserRepositoryWorkspace
{
    public Guid UserId { get; init; }
    public Guid RepositoryId { get; init; }
    public required string LocalPath { get; set; }
    public string? LastDetectedBranch { get; set; }
    public string? LastDetectedHead { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuthSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public required string TokenHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public static class SlugRules
{
    public static string Normalize(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }

    public static string KeyFromName(string name)
    {
        var letters = new string(name.Where(char.IsLetterOrDigit).ToArray());
        if (letters.Length >= 3)
        {
            return letters[..3].ToUpperInvariant();
        }

        return (letters + "XXX")[..3].ToUpperInvariant();
    }

    public static bool IsValid(string slug) =>
        !string.IsNullOrWhiteSpace(slug)
        && slug.Length is >= 2 and <= 64
        && slug.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
        && !slug.StartsWith('-')
        && !slug.EndsWith('-');

    public static bool IsValidUsername(string username) =>
        !string.IsNullOrWhiteSpace(username)
        && username.Length is >= 2 and <= 39
        && username.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        && char.IsAsciiLetterOrDigit(username[0]);
}
