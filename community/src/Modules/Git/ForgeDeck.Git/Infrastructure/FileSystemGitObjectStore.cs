using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ForgeDeck.Git.Application;

namespace ForgeDeck.Git.Infrastructure;

/// <summary>
/// Bare-repo object store backed by the system <c>git</c> CLI.
/// Commits are written to a shared objects repo; per-repository bare repos use
/// <c>objects/info/alternates</c> so refs resolve shared commit objects.
/// </summary>
public sealed class FileSystemGitObjectStore : IGitObjectStore
{
    private static readonly Regex Sha40 = new("^[0-9a-f]{40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _root;
    private readonly string _sharedGitDir;
    private readonly object _gate = new();
    private bool? _gitAvailable;
    private string? _emptyTreeSha;

    public FileSystemGitObjectStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _sharedGitDir = Path.Combine(_root, "_shared.git");
        Directory.CreateDirectory(_root);
    }

    public string GetRepositoryPath(string repositoryKey)
    {
        EnsureRepository(repositoryKey);
        return RepoPath(repositoryKey);
    }

    public void EnsureRepository(string repositoryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryKey);
        EnsureGitAvailable();
        lock (_gate)
        {
            EnsureSharedStore();
            var path = RepoPath(repositoryKey);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                RunGit(workingDirectory: _root, args: ["init", "--bare", path]);
            }

            EnsureAlternates(path);
        }
    }

    public string CreateCommit(string message, string author, string? parentSha = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        EnsureGitAvailable();
        lock (_gate)
        {
            EnsureSharedStore();
            var emptyTree = EnsureEmptyTreeObject(_sharedGitDir);

            if (!string.IsNullOrWhiteSpace(parentSha) && !Sha40.IsMatch(parentSha))
            {
                // Domain may still hold short synthetic tips from seed data; treat as root commit.
                parentSha = null;
            }

            if (!string.IsNullOrWhiteSpace(parentSha))
            {
                // Verify parent exists in the shared store (or is resolvable).
                try
                {
                    RunGit(_sharedGitDir, ["cat-file", "-e", $"{parentSha}^{{commit}}"]);
                }
                catch (InvalidOperationException)
                {
                    parentSha = null;
                }
            }

            var (name, email) = SplitAuthor(author);
            var env = new Dictionary<string, string>
            {
                ["GIT_AUTHOR_NAME"] = name,
                ["GIT_AUTHOR_EMAIL"] = email,
                ["GIT_COMMITTER_NAME"] = name,
                ["GIT_COMMITTER_EMAIL"] = email,
                ["GIT_AUTHOR_DATE"] = DateTimeOffset.UtcNow.ToString("o"),
                ["GIT_COMMITTER_DATE"] = DateTimeOffset.UtcNow.ToString("o")
            };

            var args = new List<string> { "commit-tree", emptyTree };
            if (!string.IsNullOrWhiteSpace(parentSha))
            {
                args.Add("-p");
                args.Add(parentSha);
            }

            args.Add("-m");
            args.Add(message);

            var sha = RunGit(_sharedGitDir, args, env).Trim().ToLowerInvariant();
            if (!Sha40.IsMatch(sha))
            {
                throw new InvalidOperationException($"git commit-tree returned unexpected SHA '{sha}'.");
            }

            return sha;
        }
    }

    public void UpdateRef(string repositoryKey, string refName, string sha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(refName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        EnsureRepository(repositoryKey);

        var normalized = NormalizeRef(refName);
        if (!Sha40.IsMatch(sha.ToLowerInvariant()))
        {
            throw new ArgumentException("SHA must be a 40-character hex string.", nameof(sha));
        }

        RunGit(RepoPath(repositoryKey), ["update-ref", normalized, sha.ToLowerInvariant()]);
    }

    public string? GetRef(string repositoryKey, string refName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(refName);
        var path = RepoPath(repositoryKey);
        if (!Directory.Exists(path))
        {
            return null;
        }

        var normalized = NormalizeRef(refName);
        try
        {
            var sha = RunGit(path, ["rev-parse", "--verify", normalized]).Trim().ToLowerInvariant();
            return Sha40.IsMatch(sha) ? sha : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public IReadOnlyList<GitRefTip> ListRefs(string repositoryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryKey);
        var path = RepoPath(repositoryKey);
        if (!Directory.Exists(path))
        {
            return [];
        }

        try
        {
            var output = RunGit(path, ["for-each-ref", "--format=%(objectname) %(refname)", "refs/heads", "refs/tags"]);
            var tips = new List<GitRefTip>();
            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var space = line.IndexOf(' ');
                if (space <= 0)
                {
                    continue;
                }

                var sha = line[..space].Trim().ToLowerInvariant();
                var name = line[(space + 1)..].Trim();
                if (Sha40.IsMatch(sha) && !string.IsNullOrWhiteSpace(name))
                {
                    tips.Add(new GitRefTip(name, sha));
                }
            }

            return tips;
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>True when the <c>git</c> executable can be invoked.</summary>
    public static bool IsGitAvailable()
    {
        try
        {
            var psi = CreateProcessStartInfo(["--version"]);
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start git.");
            process.WaitForExit(5_000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureGitAvailable()
    {
        _gitAvailable ??= IsGitAvailable();
        if (_gitAvailable != true)
        {
            throw new InvalidOperationException(
                "The system 'git' CLI is required for FileSystemGitObjectStore. Install Git and ensure it is on PATH.");
        }
    }

    private void EnsureSharedStore()
    {
        if (!Directory.Exists(_sharedGitDir))
        {
            Directory.CreateDirectory(_root);
            RunGit(workingDirectory: _root, args: ["init", "--bare", _sharedGitDir]);
        }

        _ = EnsureEmptyTreeObject(_sharedGitDir);
    }

    private string EnsureEmptyTreeObject(string gitDir)
    {
        if (!string.IsNullOrWhiteSpace(_emptyTreeSha))
        {
            try
            {
                RunGit(gitDir, ["cat-file", "-e", $"{_emptyTreeSha}^{{tree}}"]);
                return _emptyTreeSha;
            }
            catch (InvalidOperationException)
            {
                _emptyTreeSha = null;
            }
        }

        // write-tree against an empty index yields the repo's empty-tree object (portable across git builds).
        var indexPath = Path.Combine(Path.GetTempPath(), "forgedeck-git-empty-" + Guid.NewGuid().ToString("N") + ".index");
        try
        {
            var env = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = indexPath };
            var sha = RunGit(gitDir, ["write-tree"], env).Trim().ToLowerInvariant();
            if (!Sha40.IsMatch(sha))
            {
                throw new InvalidOperationException($"git write-tree returned unexpected SHA '{sha}'.");
            }

            _emptyTreeSha = sha;
            return sha;
        }
        finally
        {
            try
            {
                if (File.Exists(indexPath))
                {
                    File.Delete(indexPath);
                }
            }
            catch
            {
                // best-effort
            }
        }
    }

    private void EnsureAlternates(string bareRepoPath)
    {
        var infoDir = Path.Combine(bareRepoPath, "objects", "info");
        Directory.CreateDirectory(infoDir);
        var alternatesPath = Path.Combine(infoDir, "alternates");
        var sharedObjects = Path.GetFullPath(Path.Combine(_sharedGitDir, "objects")).Replace('\\', '/');
        if (File.Exists(alternatesPath))
        {
            var existing = File.ReadAllLines(alternatesPath);
            if (existing.Any(line => string.Equals(line.Trim(), sharedObjects, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
        }

        // Git alternates must be plain text without a UTF-8 BOM.
        File.AppendAllText(alternatesPath, sharedObjects + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private string RepoPath(string repositoryKey) =>
        Path.Combine(_root, SanitizeKey(repositoryKey) + ".git");

    private static string SanitizeKey(string repositoryKey)
    {
        var buffer = new StringBuilder(repositoryKey.Length);
        foreach (var ch in repositoryKey)
        {
            buffer.Append(Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch);
        }

        var sanitized = buffer.ToString().Trim('.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "repo" : sanitized;
    }

    private static string NormalizeRef(string refName) =>
        refName.StartsWith("refs/", StringComparison.Ordinal) ? refName : $"refs/heads/{refName}";

    private static (string Name, string Email) SplitAuthor(string author)
    {
        var name = author.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "ForgeDeck";
        }

        var slug = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", ".");
        slug = slug.Trim('.');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "user";
        }

        return (name, $"{slug}@forgedeck.local");
    }

    private static string RunGit(
        string? gitDir = null,
        IReadOnlyList<string>? args = null,
        IReadOnlyDictionary<string, string>? env = null,
        string? workingDirectory = null,
        string? input = null)
    {
        args ??= [];
        var argumentList = new List<string>();
        if (!string.IsNullOrWhiteSpace(gitDir))
        {
            argumentList.Add("--git-dir=" + gitDir);
        }

        argumentList.AddRange(args);

        var psi = CreateProcessStartInfo(argumentList, workingDirectory);
        if (env is not null)
        {
            foreach (var (key, value) in env)
            {
                psi.Environment[key] = value;
            }
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git.");

        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        else
        {
            process.StandardInput.Close();
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {(args.Count > 0 ? args[0] : "")} failed (exit {process.ExitCode}): {stderr.Trim()}");
        }

        return stdout;
    }

    private static ProcessStartInfo CreateProcessStartInfo(IReadOnlyList<string> args, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Path.GetTempPath()
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }
}
