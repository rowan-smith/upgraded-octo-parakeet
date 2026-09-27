using ForgeDeck.Git.Application;
using ForgeDeck.Git.Infrastructure;

namespace Core.Tests;

public sealed class FileSystemGitObjectStoreTests
{
    [Fact]
    public void CreateCommit_and_UpdateRef_write_real_git_objects()
    {
        if (!FileSystemGitObjectStore.IsGitAvailable())
        {
            return; // System git CLI is not available.
        }

        var root = Path.Combine(Path.GetTempPath(), "forgedeck-git-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemGitObjectStore(root);
            const string key = "repo-alpha";
            store.EnsureRepository(key);

            var sha = store.CreateCommit("Initial commit", "Maya Chen");
            Assert.Equal(40, sha.Length);
            Assert.Matches("^[0-9a-f]{40}$", sha);

            store.UpdateRef(key, "refs/heads/main", sha);
            Assert.Equal(sha, store.GetRef(key, "refs/heads/main"));

            var child = store.CreateCommit("Second commit", "Jamie Park", sha);
            Assert.Equal(40, child.Length);
            Assert.NotEqual(sha, child);
            store.UpdateRef(key, "refs/heads/main", child);
            Assert.Equal(child, store.GetRef(key, "main"));

            var path = store.GetRepositoryPath(key);
            Assert.True(Directory.Exists(path));
            Assert.True(File.Exists(Path.Combine(path, "HEAD")) || Directory.Exists(Path.Combine(path, "refs")));
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
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public void InMemory_store_supports_refs_and_short_shas()
    {
        var store = new InMemoryGitObjectStore();
        store.EnsureRepository("r1");
        var sha = store.CreateCommit("msg", "author");
        Assert.True(sha.Length < 40);
        store.UpdateRef("r1", "refs/heads/main", sha);
        Assert.Equal(sha, store.GetRef("r1", "main"));
        Assert.StartsWith("memory://", store.GetRepositoryPath("r1"));
    }
}
