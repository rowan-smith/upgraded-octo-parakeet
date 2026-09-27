using System.Collections.Concurrent;

namespace ForgeDeck.Git.Application;

/// <summary>
/// Git object store / ref transport. FileSystem backend writes real objects via the system <c>git</c> CLI;
/// InMemory keeps short synthetic SHAs for tests.
/// </summary>
public interface IGitObjectStore
{
    /// <summary>Creates a commit object and returns its SHA (40-char hex for FileSystem; short for InMemory).</summary>
    string CreateCommit(string message, string author, string? parentSha = null);

    void EnsureRepository(string repositoryKey);

    void UpdateRef(string repositoryKey, string refName, string sha);

    string? GetRef(string repositoryKey, string refName);

    /// <summary>Absolute path to the bare repository directory (or an in-memory URI for InMemory).</summary>
    string GetRepositoryPath(string repositoryKey);

    /// <summary>Lists refs under refs/heads and refs/tags (name is full ref path).</summary>
    IReadOnlyList<GitRefTip> ListRefs(string repositoryKey);
}

public sealed record GitRefTip(string Name, string Sha);

/// <summary>Dict-based object store used in Testing and when <c>Git:ObjectStore=InMemory</c>.</summary>
public sealed class InMemoryGitObjectStore : IGitObjectStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _refs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _paths = new(StringComparer.Ordinal);

    public string CreateCommit(string message, string author, string? parentSha = null)
    {
        _ = message;
        _ = author;
        _ = parentSha;
        return Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant()[..7];
    }

    public void EnsureRepository(string repositoryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryKey);
        _refs.GetOrAdd(repositoryKey, _ => new ConcurrentDictionary<string, string>(StringComparer.Ordinal));
        _paths.TryAdd(repositoryKey, $"memory://{repositoryKey}");
    }

    public void UpdateRef(string repositoryKey, string refName, string sha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(refName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        EnsureRepository(repositoryKey);
        _refs[repositoryKey][NormalizeRef(refName)] = sha;
    }

    public string? GetRef(string repositoryKey, string refName)
    {
        if (!_refs.TryGetValue(repositoryKey, out var refs))
        {
            return null;
        }

        return refs.TryGetValue(NormalizeRef(refName), out var sha) ? sha : null;
    }

    public string GetRepositoryPath(string repositoryKey)
    {
        EnsureRepository(repositoryKey);
        return _paths[repositoryKey];
    }

    public IReadOnlyList<GitRefTip> ListRefs(string repositoryKey)
    {
        if (!_refs.TryGetValue(repositoryKey, out var refs))
        {
            return [];
        }

        return refs.Select(kv => new GitRefTip(kv.Key, kv.Value)).OrderBy(r => r.Name, StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeRef(string refName) =>
        refName.StartsWith("refs/", StringComparison.Ordinal) ? refName : $"refs/heads/{refName}";
}
