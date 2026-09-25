using Microsoft.Extensions.Logging;

namespace ForgeDeck.Runner;

internal sealed class WorkspaceManager(RunnerOptions options, ILogger<WorkspaceManager> logger)
{
    public string CreateWorkspace(string workingDirectoryHint)
    {
        var root = Path.Combine(options.WorkRoot, workingDirectoryHint.Replace('/', Path.DirectorySeparatorChar));
        var source = Path.Combine(root, "source");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(source);
        return source;
    }

    public async Task PrepareSourceAsync(
        string sourceDir,
        string? repositoryUrl,
        string? cloneToken,
        string commitSha,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            logger.LogInformation("No repository URL; using empty workspace at {Dir}", sourceDir);
            return;
        }

        var url = repositoryUrl;
        if (!string.IsNullOrWhiteSpace(cloneToken) && Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri) { UserName = "x-access-token", Password = cloneToken };
            url = builder.Uri.ToString();
        }

        var gitDir = Path.Combine(sourceDir, ".git");
        if (!Directory.Exists(gitDir))
        {
            await RunGitAsync(sourceDir, $"clone -- {Quote(url)} .", cancellationToken);
        }
        else
        {
            await RunGitAsync(sourceDir, "fetch --all --prune", cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(commitSha) && !string.Equals(commitSha, "local", StringComparison.OrdinalIgnoreCase))
        {
            await RunGitAsync(sourceDir, $"checkout --force {Quote(commitSha)}", cancellationToken);
            var head = (await RunGitAsync(sourceDir, "rev-parse HEAD", cancellationToken)).Trim();
            if (!head.StartsWith(commitSha, StringComparison.OrdinalIgnoreCase) &&
                !commitSha.StartsWith(head, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Checked out {head} but expected {commitSha}.");
            }
        }
    }

    public void Cleanup(string sourceDir, bool failed)
    {
        if (failed && options.KeepFailedWorkspace)
        {
            logger.LogWarning("KEEP_FAILED_WORKSPACE set; leaving {Dir}", sourceDir);
            return;
        }

        try
        {
            var root = Directory.GetParent(sourceDir)?.FullName;
            if (root is not null && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Workspace cleanup failed for {Dir}", sourceDir);
        }
    }

    public static IReadOnlyList<string> MatchGlobs(string root, IEnumerable<string> globs)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var glob in globs)
        {
            if (string.IsNullOrWhiteSpace(glob))
            {
                continue;
            }

            var normalized = glob.Replace('\\', '/');
            var recursive = normalized.Contains("**", StringComparison.Ordinal);
            var filePattern = Path.GetFileName(normalized.Replace("**/", ""));
            if (string.IsNullOrWhiteSpace(filePattern))
            {
                filePattern = "*";
            }

            var searchRoot = root;
            var prefix = normalized.Contains('/') ? normalized[..normalized.LastIndexOf('/')] : "";
            prefix = prefix.Replace("**/", "").Replace("**", "").Trim('/');
            if (!string.IsNullOrEmpty(prefix) && !prefix.Contains('*'))
            {
                searchRoot = Path.Combine(root, prefix.Replace('/', Path.DirectorySeparatorChar));
            }

            if (!Directory.Exists(searchRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(searchRoot, filePattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                results.Add(file);
            }
        }
        return results.ToArray();
    }

    private async Task<string> RunGitAsync(string cwd, string arguments, CancellationToken cancellationToken)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} failed ({process.ExitCode}): {stderr}");
        }

        return stdout;
    }

    private static string Quote(string value) =>
        OperatingSystem.IsWindows() ? $"\"{value.Replace("\"", "\\\"")}\"" : $"'{value.Replace("'", "'\\''")}'";
}
