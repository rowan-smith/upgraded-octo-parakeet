using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Platform.Contracts.SourceControl;
using Platform.Core.Domain;
using Platform.Core.Persistence;

namespace Platform.Core.Application;

public sealed record SetupRequest(string OrganisationName, string? OrganisationDescription, string DisplayName, string Username, string Email, string Password);
public sealed record SetupStatus(bool Initialised, string? OrganisationName, bool HasProjects, bool HasRepositories);
public sealed record LoginResult(string Token, UserAccount User, UserProfile? Profile);
public sealed record CreateProjectRequest(string Name, string? Slug, string? Key, string? Description, ProjectVisibility Visibility = ProjectVisibility.Private);
public sealed record CreateRepositoryRequest(string Name, string? Slug, string? DefaultBranch, string ProviderType, string? ExternalRepositoryId,
    string ExternalOwner, string ExternalName, string CloneUrl, string? WebUrl);

public static class TokenHash
{
    public static string Compute(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static string CreateRaw() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

public sealed class SetupService(ITenancyStore store, IPasswordHasher<UserAccount> passwords)
{
    public SetupStatus GetStatus()
    {
        var organisation = store.GetOrganisation();
        var projects = store.ListProjects();
        return new(store.GetInstance().State == InstanceState.Initialised, organisation?.Name, projects.Count != 0,
            projects.Any(project => store.ListRepositories(project.Id).Count != 0));
    }

    public UserAccount Bootstrap(SetupRequest request)
    {
        if (store.GetInstance().State != InstanceState.Uninitialised)
            throw new InvalidOperationException("This ForgeDeck instance has already been initialised.");
        if (string.IsNullOrWhiteSpace(request.OrganisationName) || string.IsNullOrWhiteSpace(request.DisplayName))
            throw new ArgumentException("Organisation and display names are required.");
        if (!SlugRules.IsValidUsername(request.Username))
            throw new ArgumentException("Username is invalid.");
        if (request.Password.Length < 8)
            throw new ArgumentException("Password must contain at least 8 characters.");

        var now = DateTimeOffset.UtcNow;
        var organisation = new Organisation
        {
            Id = KnownIds.OrganisationId,
            Name = request.OrganisationName.Trim(),
            Description = request.OrganisationDescription?.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
        var user = new UserAccount
        {
            Id = request.Email.Equals("maya@northstar.dev", StringComparison.OrdinalIgnoreCase) ? KnownIds.MayaUserId : Guid.NewGuid(),
            Email = request.Email.Trim(),
            Username = request.Username.Trim(),
            PasswordHash = "",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwords.HashPassword(user, request.Password);
        var profile = new UserProfile { UserId = user.Id, DisplayName = request.DisplayName.Trim() };
        var membership = new OrganisationMembership { UserId = user.Id, Role = OrganisationRole.Owner, Status = MembershipStatus.Active, JoinedAt = now };
        store.Bootstrap(organisation, user, profile, membership);
        return user;
    }
}

public sealed class AuthService(ITenancyStore store, IPasswordHasher<UserAccount> passwords)
{
    public LoginResult? Login(string email, string password)
    {
        var user = store.FindUserByEmail(email);
        if (user is null || user.Status != UserStatus.Active ||
            passwords.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
            return null;

        var rawToken = TokenHash.CreateRaw();
        store.SaveSession(new AuthSession
        {
            UserId = user.Id,
            TokenHash = TokenHash.Compute(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        store.SaveUser(user);
        return new(rawToken, user, store.GetProfile(user.Id));
    }

    public void Logout(string token) => store.RevokeSession(TokenHash.Compute(token), DateTimeOffset.UtcNow);

    public UserAccount? GetUserBySessionToken(string token)
    {
        var session = store.FindSessionByTokenHash(TokenHash.Compute(token));
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        var user = store.FindUser(session.UserId);
        return user?.Status == UserStatus.Active ? user : null;
    }
}

public sealed class MembershipService(ITenancyStore store, IPasswordHasher<UserAccount> passwords)
{
    public IReadOnlyList<(UserAccount User, UserProfile? Profile, OrganisationMembership Membership)> List() =>
        store.ListMemberships().Select(m => (store.FindUser(m.UserId)!, store.GetProfile(m.UserId), m)).ToArray();

    public UserAccount Add(string email, string username, string displayName, string password, OrganisationRole role, Guid invitedBy)
    {
        if (!SlugRules.IsValidUsername(username)) throw new ArgumentException("Username is invalid.");
        if (password.Length < 8) throw new ArgumentException("Password must contain at least 8 characters.");
        var user = new UserAccount { Email = email.Trim(), Username = username.Trim(), PasswordHash = "" };
        user.PasswordHash = passwords.HashPassword(user, password);
        store.SaveUser(user);
        store.SaveProfile(new UserProfile { UserId = user.Id, DisplayName = displayName.Trim() });
        store.SaveMembership(new OrganisationMembership { UserId = user.Id, Role = role, InvitedByUserId = invitedBy });
        return user;
    }

    public void ChangeRole(Guid userId, OrganisationRole role)
    {
        var membership = store.GetMembership(userId) ?? throw new KeyNotFoundException("Membership not found.");
        EnsureOwnerRemains(membership, role, membership.Status);
        membership.Role = role;
        store.SaveMembership(membership);
    }

    public void ChangeStatus(Guid userId, MembershipStatus status)
    {
        var membership = store.GetMembership(userId) ?? throw new KeyNotFoundException("Membership not found.");
        EnsureOwnerRemains(membership, membership.Role, status);
        membership.Status = status;
        store.SaveMembership(membership);

        var user = store.FindUser(userId) ?? throw new KeyNotFoundException("User not found.");
        user.Status = status switch
        {
            MembershipStatus.Suspended => UserStatus.Suspended,
            MembershipStatus.Active => UserStatus.Active,
            _ => user.Status
        };
        user.UpdatedAt = DateTimeOffset.UtcNow;
        store.SaveUser(user);
    }

    public void Remove(Guid userId)
    {
        var membership = store.GetMembership(userId) ?? throw new KeyNotFoundException("Membership not found.");
        EnsureOwnerRemains(membership, OrganisationRole.Member, MembershipStatus.Suspended);
        foreach (var team in store.ListUserTeamMemberships(userId))
            store.DeleteTeamMembership(team.TeamId, userId);
        foreach (var project in store.ListProjects())
            store.DeleteProjectUserAccess(project.Id, userId);
        store.DeleteMembership(userId);

        var user = store.FindUser(userId);
        if (user is null) return;
        user.Status = UserStatus.Disabled;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        store.SaveUser(user);
    }

    private void EnsureOwnerRemains(OrganisationMembership current, OrganisationRole newRole, MembershipStatus newStatus)
    {
        if (current.Role != OrganisationRole.Owner || current.Status != MembershipStatus.Active ||
            (newRole == OrganisationRole.Owner && newStatus == MembershipStatus.Active)) return;
        if (store.ListMemberships().Count(m => m.Role == OrganisationRole.Owner && m.Status == MembershipStatus.Active) <= 1)
            throw new InvalidOperationException("The last active owner cannot be demoted, suspended, or removed.");
    }
}

public sealed class ProjectService(ITenancyStore store)
{
    public Project CreateProject(CreateProjectRequest request, Guid userId, Guid? id = null)
    {
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? SlugRules.Normalize(request.Name) : SlugRules.Normalize(request.Slug);
        if (!SlugRules.IsValid(slug)) throw new ArgumentException("Project slug is invalid.");
        var project = new Project
        {
            Id = id ?? Guid.NewGuid(),
            Name = request.Name.Trim(),
            Slug = slug,
            Key = string.IsNullOrWhiteSpace(request.Key) ? SlugRules.KeyFromName(request.Name) : request.Key.Trim().ToUpperInvariant(),
            Description = request.Description?.Trim(),
            Visibility = request.Visibility,
            CreatedByUserId = userId
        };
        store.SaveProject(project);
        store.SaveProjectUserAccess(new ProjectUserAccess { ProjectId = project.Id, UserId = userId });
        return project;
    }

    public IReadOnlyList<Project> ListProjects() => store.ListProjects();

    public IReadOnlyList<Project> ListAccessible(Guid userId, OrganisationRole role)
    {
        var access = new ProjectAccessService(store);
        return store.ListProjects().Where(project => access.CanAccess(project, userId, role)).ToArray();
    }

    public Project? GetBySlug(string slug) => store.FindProjectBySlug(slug);
    public Project? Get(Guid id) => store.FindProject(id);
    public void Delete(Guid id) => store.DeleteProject(id);

    public void GrantUser(Guid projectId, Guid userId)
    {
        if (store.FindProject(projectId) is null) throw new KeyNotFoundException("Project not found.");
        if (store.FindUser(userId) is null) throw new KeyNotFoundException("User not found.");
        store.SaveProjectUserAccess(new ProjectUserAccess { ProjectId = projectId, UserId = userId });
    }

    public void RevokeUser(Guid projectId, Guid userId) => store.DeleteProjectUserAccess(projectId, userId);

    public void GrantTeam(Guid projectId, Guid teamId)
    {
        if (store.FindProject(projectId) is null) throw new KeyNotFoundException("Project not found.");
        if (store.FindTeam(teamId) is null) throw new KeyNotFoundException("Team not found.");
        store.SaveProjectTeamAccess(new ProjectTeamAccess { ProjectId = projectId, TeamId = teamId });
    }

    public void RevokeTeam(Guid projectId, Guid teamId) => store.DeleteProjectTeamAccess(projectId, teamId);
}

public sealed class ProjectAccessService(ITenancyStore store)
{
    public bool CanAccess(Project project, Guid userId, OrganisationRole role)
    {
        if (role is OrganisationRole.Owner or OrganisationRole.Admin) return true;
        if (project.Visibility == ProjectVisibility.Organisation) return true;
        if (store.HasProjectUserAccess(project.Id, userId)) return true;
        return store.ListUserTeamMemberships(userId).Any(membership => store.HasProjectTeamAccess(project.Id, membership.TeamId));
    }

    public void EnsureAccess(Project project, Guid userId, OrganisationRole role)
    {
        if (!CanAccess(project, userId, role))
            throw new UnauthorizedAccessException("You do not have access to this project.");
    }
}

public sealed class TeamService(ITenancyStore store)
{
    public Team Create(string name, string? slug, string? description)
    {
        var normalized = string.IsNullOrWhiteSpace(slug) ? SlugRules.Normalize(name) : SlugRules.Normalize(slug);
        if (!SlugRules.IsValid(normalized)) throw new ArgumentException("Team slug is invalid.");
        if (store.FindTeamBySlug(normalized) is not null) throw new ArgumentException("Team slug is already in use.");
        var team = new Team { Name = name.Trim(), Slug = normalized, Description = description?.Trim() };
        store.SaveTeam(team);
        return team;
    }

    public Team Update(Guid id, string? name, string? slug, string? description)
    {
        var team = store.FindTeam(id) ?? throw new KeyNotFoundException("Team not found.");
        if (!string.IsNullOrWhiteSpace(name)) team.Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var normalized = SlugRules.Normalize(slug);
            if (!SlugRules.IsValid(normalized)) throw new ArgumentException("Team slug is invalid.");
            var existing = store.FindTeamBySlug(normalized);
            if (existing is not null && existing.Id != id) throw new ArgumentException("Team slug is already in use.");
            team.Slug = normalized;
        }
        if (description is not null) team.Description = description.Trim();
        team.UpdatedAt = DateTimeOffset.UtcNow;
        store.SaveTeam(team);
        return team;
    }

    public void Delete(Guid id) => store.DeleteTeam(id);
    public IReadOnlyList<Team> List() => store.ListTeams();
    public Team? Get(Guid id) => store.FindTeam(id);

    public void AddMember(Guid teamId, Guid userId)
    {
        var team = store.FindTeam(teamId) ?? throw new KeyNotFoundException("Team not found.");
        var membership = store.GetMembership(userId) ?? throw new KeyNotFoundException("User is not an organisation member.");
        if (membership.Status != MembershipStatus.Active) throw new InvalidOperationException("Only active organisation members can join teams.");
        store.SaveTeamMembership(new TeamMembership { TeamId = team.Id, UserId = userId });
    }

    public void RemoveMember(Guid teamId, Guid userId) => store.DeleteTeamMembership(teamId, userId);

    public IReadOnlyList<(UserAccount User, UserProfile? Profile)> ListMembers(Guid teamId) =>
        store.ListTeamMemberships(teamId)
            .Select(item => (store.FindUser(item.UserId)!, store.GetProfile(item.UserId)))
            .Where(item => item.Item1 is not null)
            .ToArray();
}

public sealed record InvitationCreated(Invitation Invitation, string RawToken);
public sealed record InvitationPreview(string Email, OrganisationRole Role, DateTimeOffset ExpiresAt, bool Expired, bool Accepted);

public sealed class InvitationService(ITenancyStore store, IPasswordHasher<UserAccount> passwords)
{
    public InvitationCreated Create(string email, OrganisationRole role, Guid invitedByUserId, TimeSpan? lifetime = null)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required.");
        if (role == OrganisationRole.Owner)
            throw new ArgumentException("Owners cannot be invited directly; promote an existing member.");
        var raw = TokenHash.CreateRaw();
        var invitation = new Invitation
        {
            Email = email.Trim(),
            Role = role,
            TokenHash = TokenHash.Compute(raw),
            InvitedByUserId = invitedByUserId,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromDays(14))
        };
        store.SaveInvitation(invitation);
        return new(invitation, raw);
    }

    public InvitationPreview Preview(string rawToken)
    {
        var invitation = RequireValid(rawToken, requireOpen: false);
        return new(invitation.Email, invitation.Role, invitation.ExpiresAt,
            invitation.ExpiresAt <= DateTimeOffset.UtcNow, invitation.AcceptedAt is not null);
    }

    public UserAccount Accept(string rawToken, string username, string displayName, string password)
    {
        var invitation = RequireValid(rawToken, requireOpen: true);
        if (!SlugRules.IsValidUsername(username)) throw new ArgumentException("Username is invalid.");
        if (password.Length < 8) throw new ArgumentException("Password must contain at least 8 characters.");

        var existing = store.FindUserByEmail(invitation.Email);
        UserAccount user;
        if (existing is null)
        {
            user = new UserAccount { Email = invitation.Email, Username = username.Trim(), PasswordHash = "" };
            user.PasswordHash = passwords.HashPassword(user, password);
            store.SaveUser(user);
            store.SaveProfile(new UserProfile { UserId = user.Id, DisplayName = displayName.Trim() });
        }
        else
        {
            user = existing;
            if (user.Status != UserStatus.Active)
            {
                user.Status = UserStatus.Active;
                user.UpdatedAt = DateTimeOffset.UtcNow;
                store.SaveUser(user);
            }
            if (store.GetProfile(user.Id) is null)
                store.SaveProfile(new UserProfile { UserId = user.Id, DisplayName = displayName.Trim() });
        }

        store.SaveMembership(new OrganisationMembership
        {
            UserId = user.Id,
            Role = invitation.Role,
            Status = MembershipStatus.Active,
            InvitedByUserId = invitation.InvitedByUserId
        });
        invitation.AcceptedAt = DateTimeOffset.UtcNow;
        store.SaveInvitation(invitation);
        return user;
    }

    public IReadOnlyList<Invitation> List() => store.ListInvitations();

    private Invitation RequireValid(string rawToken, bool requireOpen)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) throw new ArgumentException("Invitation token is required.");
        var invitation = store.FindInvitationByTokenHash(TokenHash.Compute(rawToken))
            ?? throw new KeyNotFoundException("Invitation not found.");
        if (requireOpen)
        {
            if (invitation.AcceptedAt is not null) throw new InvalidOperationException("Invitation has already been used.");
            if (invitation.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Invitation has expired.");
        }
        return invitation;
    }
}

public sealed class RepositoryService(ITenancyStore store, ISourceConnectionStore sourceConnections)
{
    public Repository CreateRepository(Guid projectId, CreateRepositoryRequest request)
    {
        if (store.FindProject(projectId) is null) throw new KeyNotFoundException("Project not found.");
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? SlugRules.Normalize(request.Name) : SlugRules.Normalize(request.Slug);
        if (!SlugRules.IsValid(slug)) throw new ArgumentException("Repository slug is invalid.");
        var repository = new Repository
        {
            ProjectId = projectId,
            Name = request.Name.Trim(),
            Slug = slug,
            DefaultBranch = request.DefaultBranch ?? "main"
        };
        var connection = new RepositoryConnection
        {
            RepositoryId = repository.Id,
            ProviderType = request.ProviderType,
            ExternalRepositoryId = request.ExternalRepositoryId,
            ExternalOwner = request.ExternalOwner,
            ExternalName = request.ExternalName,
            CloneUrl = request.CloneUrl,
            WebUrl = request.WebUrl
        };
        store.SaveRepository(repository);
        store.SaveRepositoryConnection(connection);
        if (request.ProviderType.Equals("github", StringComparison.OrdinalIgnoreCase))
        {
            sourceConnections.Save(new SourceRepositoryConnection(repository.Id, projectId,
                new RepositoryId("github", request.ExternalOwner, request.ExternalName),
                repository.DefaultBranch ?? "main", request.WebUrl ?? request.CloneUrl, DateTimeOffset.UtcNow));
        }
        return repository;
    }

    public IReadOnlyList<Repository> List(Guid projectId) => store.ListRepositories(projectId);
    public void AssociateWorkspace(Guid userId, Guid repositoryId, string localPath) =>
        store.SaveWorkspace(new UserRepositoryWorkspace { UserId = userId, RepositoryId = repositoryId, LocalPath = localPath });
    public void Delete(Guid id) => store.DeleteRepository(id);
}
