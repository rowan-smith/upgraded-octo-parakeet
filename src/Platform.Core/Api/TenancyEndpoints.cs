using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Platform.Core.Application;
using Platform.Core.Context;
using Platform.Core.Domain;
using Platform.Core.Identity;
using Platform.Core.Persistence;

namespace Platform.Core.Api;

public static class TenancyEndpoints
{
    public static void MapTenancyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/setup/status", (SetupService setup) => Results.Ok(setup.GetStatus()));
        app.MapPost("/api/setup", (SetupRequest request, SetupService setup, AuthService auth, ITenancyStore store, PlatformContextStore context) =>
        {
            try
            {
                var user = setup.Bootstrap(request);
                var login = auth.Login(request.Email, request.Password)
                             ?? throw new InvalidOperationException("Setup succeeded but login failed.");
                var organisation = store.GetOrganisation()!;
                context.Apply(
                    new OrganisationView(organisation.Id, organisation.Name, SlugRules.Normalize(organisation.Name)),
                    context.Project,
                    PlatformContextStore.ToPlatformUser(user, store.GetProfile(user.Id), OrganisationRole.Owner));
                return Results.Ok(new { token = login.Token, user = login.User, profile = login.Profile, organisation });
            }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        app.MapGet("/api/users/me", (PlatformContextStore context, ITenancyStore store) =>
            Results.Ok(new { user = store.FindUser(context.User.Id), profile = store.GetProfile(context.User.Id), membership = store.GetMembership(context.User.Id) }));

        app.MapGet("/api/organisation", (ITenancyStore store) =>
            store.GetOrganisation() is { } organisation ? Results.Ok(organisation) : Results.NotFound());
        app.MapPatch("/api/organisation", (UpdateOrganisationRequest request, ITenancyStore store, PlatformContextStore context, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.OrganisationManage) &&
                !authorizer.Has(http, OrganisationPermissions.UsersManage))
                return PermissionAuthorizer.Forbidden();
            var organisation = store.GetOrganisation();
            if (organisation is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(request.Name)) organisation.Name = request.Name.Trim();
            if (request.Description is not null) organisation.Description = request.Description.Trim();
            if (request.AvatarUrl is not null) organisation.AvatarUrl = request.AvatarUrl;
            organisation.UpdatedAt = DateTimeOffset.UtcNow;
            store.SaveOrganisation(organisation);
            context.Apply(new OrganisationView(organisation.Id, organisation.Name, SlugRules.Normalize(organisation.Name)), context.Project, context.User);
            return Results.Ok(organisation);
        });

        app.MapGet("/api/organisation/members", (MembershipService memberships, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersRead)) return PermissionAuthorizer.Forbidden();
            return Results.Ok(memberships.List().Select(item => new { item.User, item.Profile, item.Membership }));
        });
        app.MapPost("/api/organisation/members", (CreateMemberRequest request, MembershipService memberships, PlatformContextStore context, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersManage)) return PermissionAuthorizer.Forbidden();
            try { return Results.Ok(memberships.Add(request.Email, request.Username, request.DisplayName, request.Password, request.Role, context.User.Id)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        app.MapPatch("/api/organisation/members/{userId:guid}/role", (Guid userId, ChangeRoleRequest request, MembershipService memberships, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersManage)) return PermissionAuthorizer.Forbidden();
            if (request.Role == OrganisationRole.Owner && !authorizer.Has(http, OrganisationPermissions.OrganisationManage))
                return PermissionAuthorizer.Forbidden();
            try { memberships.ChangeRole(userId, request.Role); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
        app.MapPatch("/api/organisation/members/{userId:guid}/status", (Guid userId, ChangeStatusRequest request, MembershipService memberships, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersManage)) return PermissionAuthorizer.Forbidden();
            try { memberships.ChangeStatus(userId, request.Status); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
        app.MapDelete("/api/organisation/members/{userId:guid}", (Guid userId, MembershipService memberships, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersManage)) return PermissionAuthorizer.Forbidden();
            try { memberships.Remove(userId); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        app.MapGet("/api/organisation/invitations", (InvitationService invitations, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersManage)) return PermissionAuthorizer.Forbidden();
            return Results.Ok(invitations.List().Select(item => new
            {
                item.Id, item.Email, item.Role, item.InvitedByUserId, item.ExpiresAt, item.AcceptedAt, item.CreatedAt
            }));
        });
        app.MapPost("/api/organisation/invitations", (CreateInvitationRequest request, InvitationService invitations, PlatformContextStore context, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersManage)) return PermissionAuthorizer.Forbidden();
            try
            {
                var created = invitations.Create(request.Email, request.Role, context.User.Id);
                return Results.Ok(new
                {
                    created.Invitation.Id,
                    created.Invitation.Email,
                    created.Invitation.Role,
                    created.Invitation.ExpiresAt,
                    token = created.RawToken,
                    acceptPath = $"/#/invite/{Uri.EscapeDataString(created.RawToken)}"
                });
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        app.MapGet("/api/invitations/{token}", (string token, InvitationService invitations) =>
        {
            try { return Results.Ok(invitations.Preview(token)); }
            catch (KeyNotFoundException) { return Results.NotFound(new { error = "Invitation not found." }); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        app.MapPost("/api/invitations/{token}/accept", (string token, AcceptInvitationRequest request, InvitationService invitations, AuthService auth) =>
        {
            try
            {
                var user = invitations.Accept(token, request.Username, request.DisplayName, request.Password);
                var login = auth.Login(user.Email, request.Password)
                             ?? throw new InvalidOperationException("Invitation accepted but login failed.");
                return Results.Ok(new { token = login.Token, user = login.User, profile = login.Profile });
            }
            catch (KeyNotFoundException) { return Results.NotFound(new { error = "Invitation not found." }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        app.MapGet("/api/teams", (TeamService teams, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersRead)) return PermissionAuthorizer.Forbidden();
            return Results.Ok(teams.List());
        });
        app.MapGet("/api/teams/{id:guid}", (Guid id, TeamService teams, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersRead)) return PermissionAuthorizer.Forbidden();
            var team = teams.Get(id);
            if (team is null) return Results.NotFound();
            return Results.Ok(new { team, members = teams.ListMembers(id).Select(m => new { m.User, m.Profile }) });
        });
        app.MapPost("/api/teams", (CreateTeamRequest request, TeamService teams, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.TeamsManage)) return PermissionAuthorizer.Forbidden();
            try { return Results.Ok(teams.Create(request.Name, request.Slug, request.Description)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        app.MapPatch("/api/teams/{id:guid}", (Guid id, UpdateTeamRequest request, TeamService teams, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.TeamsManage)) return PermissionAuthorizer.Forbidden();
            try { return Results.Ok(teams.Update(id, request.Name, request.Slug, request.Description)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
        app.MapDelete("/api/teams/{id:guid}", (Guid id, TeamService teams, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.TeamsManage)) return PermissionAuthorizer.Forbidden();
            teams.Delete(id);
            return Results.NoContent();
        });
        app.MapPost("/api/teams/{id:guid}/members", (Guid id, TeamMemberRequest request, TeamService teams, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.TeamsManage)) return PermissionAuthorizer.Forbidden();
            try { teams.AddMember(id, request.UserId); return Results.NoContent(); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
        app.MapDelete("/api/teams/{id:guid}/members/{userId:guid}", (Guid id, Guid userId, TeamService teams, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.TeamsManage)) return PermissionAuthorizer.Forbidden();
            teams.RemoveMember(id, userId);
            return Results.NoContent();
        });

        app.MapGet("/api/projects", (ProjectService projects, ITenancyStore store, PlatformContextStore context) =>
        {
            var membership = store.GetMembership(context.User.Id);
            var role = membership?.Role ?? OrganisationRole.Member;
            return Results.Ok(projects.ListAccessible(context.User.Id, role));
        });
        app.MapGet("/api/projects/{slug}", (string slug, ProjectService projects, ProjectAccessService access, ITenancyStore store, PlatformContextStore context) =>
        {
            var project = projects.GetBySlug(slug);
            if (project is null) return Results.NotFound();
            var role = store.GetMembership(context.User.Id)?.Role ?? OrganisationRole.Member;
            try { access.EnsureAccess(project, context.User.Id, role); }
            catch (UnauthorizedAccessException) { return PermissionAuthorizer.Forbidden(); }
            return Results.Ok(project);
        });
        app.MapPost("/api/projects", (CreateProjectRequest request, ProjectService projects, PlatformContextStore context, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ProjectsCreate)) return PermissionAuthorizer.Forbidden();
            try { return Results.Ok(projects.CreateProject(request, context.User.Id)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        app.MapPatch("/api/projects/{id:guid}", (Guid id, UpdateProjectRequest request, ITenancyStore store, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ProjectsManage)) return PermissionAuthorizer.Forbidden();
            var project = store.FindProject(id);
            if (project is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(request.Name)) project.Name = request.Name.Trim();
            if (!string.IsNullOrWhiteSpace(request.Slug))
            {
                var slug = SlugRules.Normalize(request.Slug);
                if (!SlugRules.IsValid(slug)) return Results.BadRequest(new { error = "Project slug is invalid." });
                project.Slug = slug;
            }
            if (request.Description is not null) project.Description = request.Description;
            if (request.Visibility is not null) project.Visibility = request.Visibility.Value;
            project.UpdatedAt = DateTimeOffset.UtcNow;
            store.SaveProject(project);
            return Results.Ok(project);
        });
        app.MapDelete("/api/projects/{id:guid}", (Guid id, ProjectService projects, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ProjectsManage)) return PermissionAuthorizer.Forbidden();
            projects.Delete(id);
            return Results.NoContent();
        });

        app.MapPost("/api/projects/{projectId:guid}/members", (Guid projectId, ProjectMemberRequest request, ProjectService projects, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ProjectsManage)) return PermissionAuthorizer.Forbidden();
            try { projects.GrantUser(projectId, request.UserId); return Results.NoContent(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
        app.MapDelete("/api/projects/{projectId:guid}/members/{userId:guid}", (Guid projectId, Guid userId, ProjectService projects, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ProjectsManage)) return PermissionAuthorizer.Forbidden();
            projects.RevokeUser(projectId, userId);
            return Results.NoContent();
        });
        app.MapPost("/api/projects/{projectId:guid}/teams", (Guid projectId, ProjectTeamRequest request, ProjectService projects, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ProjectsManage)) return PermissionAuthorizer.Forbidden();
            try { projects.GrantTeam(projectId, request.TeamId); return Results.NoContent(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
        app.MapDelete("/api/projects/{projectId:guid}/teams/{teamId:guid}", (Guid projectId, Guid teamId, ProjectService projects, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ProjectsManage)) return PermissionAuthorizer.Forbidden();
            projects.RevokeTeam(projectId, teamId);
            return Results.NoContent();
        });

        app.MapGet("/api/projects/{projectId:guid}/repositories", (Guid projectId, RepositoryService repositories, ProjectAccessService access, ITenancyStore store, PlatformContextStore context) =>
        {
            var project = store.FindProject(projectId);
            if (project is null) return Results.NotFound();
            var role = store.GetMembership(context.User.Id)?.Role ?? OrganisationRole.Member;
            try { access.EnsureAccess(project, context.User.Id, role); }
            catch (UnauthorizedAccessException) { return PermissionAuthorizer.Forbidden(); }
            return Results.Ok(repositories.List(projectId));
        });
        app.MapPost("/api/projects/{projectId:guid}/repositories", (Guid projectId, CreateRepositoryRequest request, RepositoryService repositories, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ProjectsManage) &&
                !authorizer.Has(http, "source.repository.connect"))
                return PermissionAuthorizer.Forbidden();
            try { return Results.Ok(repositories.CreateRepository(projectId, request)); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        app.MapDelete("/api/projects/{projectId:guid}/repositories/{id:guid}", (Guid projectId, Guid id, RepositoryService repositories, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ProjectsManage)) return PermissionAuthorizer.Forbidden();
            repositories.Delete(id);
            return Results.NoContent();
        });
        app.MapPost("/api/projects/{projectId:guid}/repositories/{id:guid}/workspace", (Guid projectId, Guid id, WorkspaceRequest request,
            RepositoryService repositories, PlatformContextStore context) =>
        {
            repositories.AssociateWorkspace(context.User.Id, id, request.LocalPath);
            return Results.NoContent();
        });

        app.MapPost("/api/core/context/project", (SwitchProjectRequest request, ITenancyStore store, PlatformContextStore context, ProjectAccessService access) =>
        {
            var project = store.FindProject(request.ProjectId);
            if (project is null) return Results.NotFound();
            var role = store.GetMembership(context.User.Id)?.Role ?? OrganisationRole.Member;
            try { access.EnsureAccess(project, context.User.Id, role); }
            catch (UnauthorizedAccessException) { return PermissionAuthorizer.Forbidden(); }
            var profile = store.GetProfile(context.User.Id);
            if (profile is not null)
            {
                profile.DefaultProjectId = project.Id;
                store.SaveProfile(profile);
            }
            var repository = store.ListRepositories(project.Id).FirstOrDefault();
            context.SetProject(new ProjectView(project.Id, KnownIds.OrganisationId, project.Name, project.Key, repository?.Name));
            return Results.Ok(context.Project);
        });
    }
}

public sealed record UpdateOrganisationRequest(string? Name, string? Description, string? AvatarUrl);
public sealed record CreateMemberRequest(string Email, string Username, string DisplayName, string Password, OrganisationRole Role);
public sealed record ChangeRoleRequest(OrganisationRole Role);
public sealed record ChangeStatusRequest(MembershipStatus Status);
public sealed record UpdateProjectRequest(string? Name, string? Slug, string? Description, ProjectVisibility? Visibility);
public sealed record WorkspaceRequest(string LocalPath);
public sealed record SwitchProjectRequest(Guid ProjectId);
public sealed record CreateInvitationRequest(string Email, OrganisationRole Role);
public sealed record AcceptInvitationRequest(string Username, string DisplayName, string Password);
public sealed record CreateTeamRequest(string Name, string? Slug, string? Description);
public sealed record UpdateTeamRequest(string? Name, string? Slug, string? Description);
public sealed record TeamMemberRequest(Guid UserId);
public sealed record ProjectMemberRequest(Guid UserId);
public sealed record ProjectTeamRequest(Guid TeamId);
