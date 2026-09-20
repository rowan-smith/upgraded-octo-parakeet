using Platform.Core.Application;
using Platform.Core.Domain;
using Platform.Contracts.SourceControl;
using Platform.Core.Persistence;

namespace Tests.Unit;

public sealed class RepositoryServiceTests
{
    [Fact]
    public void Create_repository_with_connection_and_user_workspace()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Platform", "platform", null, null), owner.Id);
        var repositories = new RepositoryService(fixture.Store, new NullSourceConnectionStore());

        var repository = repositories.CreateRepository(project.Id, new CreateRepositoryRequest(
            "platform", "platform", "main", "github", "1", "example", "platform",
            "https://github.com/example/platform.git", "https://github.com/example/platform"));

        Assert.Equal(RepositoryStatus.Connected, repository.Status);
        Assert.Equal("github", fixture.Store.GetRepositoryConnection(repository.Id)!.ProviderType);

        repositories.AssociateWorkspace(owner.Id, repository.Id, @"C:\Development\Platform");
        var workspace = fixture.Store.GetWorkspace(owner.Id, repository.Id);
        Assert.Equal(@"C:\Development\Platform", workspace!.LocalPath);

        var other = fixture.Memberships.Add("alice@example.com", "alice", "Alice", "password123", OrganisationRole.Member, owner.Id);
        repositories.AssociateWorkspace(other.Id, repository.Id, @"D:\Work\platform");
        Assert.Equal(@"D:\Work\platform", fixture.Store.GetWorkspace(other.Id, repository.Id)!.LocalPath);
        Assert.Equal(@"C:\Development\Platform", fixture.Store.GetWorkspace(owner.Id, repository.Id)!.LocalPath);
    }

    [Fact]
    public void Repository_slug_unique_within_project()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        var project = fixture.Projects.CreateProject(new CreateProjectRequest("Platform", "platform", null, null), owner.Id);
        var repositories = new RepositoryService(fixture.Store, new NullSourceConnectionStore());
        repositories.CreateRepository(project.Id, new CreateRepositoryRequest(
            "api", "api", "main", "github", null, "example", "api", "https://example/api.git", null));

        Assert.ThrowsAny<Exception>(() => repositories.CreateRepository(project.Id, new CreateRepositoryRequest(
            "api-2", "api", "main", "github", null, "example", "api-2", "https://example/api2.git", null)));
    }

    private sealed class NullSourceConnectionStore : ISourceConnectionStore
    {
        public IReadOnlyList<SourceRepositoryConnection> List(Guid projectId) => [];
        public SourceRepositoryConnection? Find(Guid id) => null;
        public SourceRepositoryConnection? Find(Guid projectId, RepositoryId repositoryId) => null;
        public void Save(SourceRepositoryConnection connection) { }
    }
}
