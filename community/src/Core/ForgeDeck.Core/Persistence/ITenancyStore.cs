using ForgeDeck.Core.Domain;

namespace ForgeDeck.Core.Persistence;

public interface ITenancyStore
{
    InstanceConfiguration GetInstance();
    void SaveInstance(InstanceConfiguration configuration);
    void EnsureInstanceId();

    Organisation? GetOrganisation();
    void Bootstrap(Organisation organisation, UserAccount user, UserProfile profile, OrganisationMembership membership);
    void SaveOrganisation(Organisation organisation);
    void CreateOwner(UserAccount user, UserProfile profile, OrganisationMembership membership);

    UserAccount? FindUser(Guid id);
    UserAccount? FindUserByEmail(string email);
    UserAccount? FindUserByUsername(string username);
    IReadOnlyList<UserAccount> ListUsers();
    void SaveUser(UserAccount user);
    UserProfile? GetProfile(Guid userId);
    void SaveProfile(UserProfile profile);

    OrganisationMembership? GetMembership(Guid userId);
    IReadOnlyList<OrganisationMembership> ListMemberships();
    void SaveMembership(OrganisationMembership membership);
    void DeleteMembership(Guid userId);

    void SaveInvitation(Invitation invitation);
    Invitation? FindInvitation(Guid id);
    Invitation? FindInvitationByTokenHash(string tokenHash);
    IReadOnlyList<Invitation> ListInvitations();

    void SaveTeam(Team team);
    Team? FindTeam(Guid id);
    Team? FindTeamBySlug(string slug);
    IReadOnlyList<Team> ListTeams();
    void DeleteTeam(Guid id);
    void SaveTeamMembership(TeamMembership membership);
    void DeleteTeamMembership(Guid teamId, Guid userId);
    IReadOnlyList<TeamMembership> ListTeamMemberships(Guid teamId);
    IReadOnlyList<TeamMembership> ListUserTeamMemberships(Guid userId);

    void SaveProject(Project project);
    Project? FindProject(Guid id);
    Project? FindProjectBySlug(string slug);
    IReadOnlyList<Project> ListProjects();
    void DeleteProject(Guid id);
    IReadOnlyList<Guid> ListStarredProjectIds(Guid userId);
    bool IsProjectStarred(Guid userId, Guid projectId);
    void StarProject(Guid userId, Guid projectId);
    void UnstarProject(Guid userId, Guid projectId);
    void SaveProjectUserAccess(ProjectUserAccess access);
    void DeleteProjectUserAccess(Guid projectId, Guid userId);
    IReadOnlyList<ProjectUserAccess> ListProjectUserAccess(Guid projectId);
    bool HasProjectUserAccess(Guid projectId, Guid userId);
    void SaveProjectTeamAccess(ProjectTeamAccess access);
    void DeleteProjectTeamAccess(Guid projectId, Guid teamId);
    IReadOnlyList<ProjectTeamAccess> ListProjectTeamAccess(Guid projectId);
    bool HasProjectTeamAccess(Guid projectId, Guid teamId);

    IReadOnlyList<ProjectModuleSetting> ListProjectModules(Guid projectId);
    void ReplaceProjectModules(Guid projectId, IReadOnlyList<ProjectModuleSetting> modules);
    bool IsProjectModuleEnabled(Guid projectId, string extensionId);

    void SaveRepository(Repository repository);
    Repository? FindRepository(Guid id);
    IReadOnlyList<Repository> ListRepositories(Guid projectId);
    void DeleteRepository(Guid id);
    void SaveRepositoryConnection(RepositoryConnection connection);
    RepositoryConnection? GetRepositoryConnection(Guid repositoryId);
    void SaveWorkspace(UserRepositoryWorkspace workspace);
    UserRepositoryWorkspace? GetWorkspace(Guid userId, Guid repositoryId);

    void SaveSession(AuthSession session);
    AuthSession? FindSessionByTokenHash(string tokenHash);
    void RevokeSession(string tokenHash, DateTimeOffset revokedAt);

    void SaveBootstrapSession(BootstrapSession session);
    BootstrapSession? FindBootstrapSession(string tokenHash);
    void RevokeBootstrapSession(string tokenHash, DateTimeOffset revokedAt);
    void RevokeAllBootstrapSessions(DateTimeOffset revokedAt);

    LicenceRecord? GetActiveLicence();
    IReadOnlyList<LicenceRecord> ListLicences();
    void SaveLicence(LicenceRecord record);
    void ReplaceActiveLicence(LicenceRecord record);
    void MarkActiveLicence(LicenceRecordStatus status);
    void AddLicenceHistory(LicenceHistoryEntry entry);
    IReadOnlyList<LicenceHistoryEntry> ListLicenceHistory();
}
