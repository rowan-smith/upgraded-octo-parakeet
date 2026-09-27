using System.Text;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Git.Api;
using ForgeDeck.Git.Application;
using ForgeDeck.Git.Domain;
using ForgeDeck.Git.Infrastructure;

namespace Core.Tests;

public sealed class GitSmartHttpTransportTests
{
    [Fact]
    public async Task Advertise_upload_pack_returns_service_header_and_refs()
    {
        if (!FileSystemGitObjectStore.IsGitAvailable())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "forgedeck-smart-http", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemGitObjectStore(root);
            var repos = new MemoryGitRepositoryStore();
            var events = new NoOpEventPublisher();
            var service = new GitRepositoryService(repos, events, store);
            var repository = service.Create("Smart Http Demo");
            var transport = new GitSmartHttpTransport(store, service);

            Assert.True(transport.IsSupported);
            var (contentType, body) = await transport.AdvertiseAsync(repository.Id, "git-upload-pack");
            Assert.Equal("application/x-git-upload-pack-advertisement", contentType);
            var text = Encoding.UTF8.GetString(body);
            Assert.Contains("# service=git-upload-pack", text, StringComparison.Ordinal);
            Assert.Contains("refs/heads/main", text, StringComparison.Ordinal);

            var clone = service.GetCloneUrl(repository.Id, "https://git.example.test");
            Assert.NotNull(clone);
            Assert.Contains($"/api/git/repositories/{repository.Id}", clone!.CloneCommand, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // best-effort
            }
        }
    }

    [Fact]
    public void InMemory_store_reports_smart_http_unsupported()
    {
        var store = new InMemoryGitObjectStore();
        var repos = new MemoryGitRepositoryStore();
        var service = new GitRepositoryService(repos, new NoOpEventPublisher(), store);
        var transport = new GitSmartHttpTransport(store, service);
        Assert.False(transport.IsSupported);
    }

    private sealed class MemoryGitRepositoryStore : IGitRepositoryStore
    {
        private readonly Dictionary<Guid, GitRepository> _items = new();

        public IReadOnlyList<GitRepository> List() => _items.Values.ToArray();

        public GitRepository? Find(Guid id) =>
            _items.TryGetValue(id, out var item) ? item : null;

        public GitRepository? Find(string slug) =>
            _items.Values.FirstOrDefault(r => string.Equals(r.Slug, slug, StringComparison.OrdinalIgnoreCase));

        public void Add(GitRepository repository) => _items[repository.Id] = repository;

        public void Update(GitRepository repository) => _items[repository.Id] = repository;
    }

    private sealed class NoOpEventPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent data, PublishOptions? options = null, CancellationToken cancellationToken = default)
            where TEvent : class => Task.CompletedTask;
    }
}
