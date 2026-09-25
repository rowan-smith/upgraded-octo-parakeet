using System.Diagnostics;
using System.Text.RegularExpressions;
using ForgeDeck.Contracts.SourceControl;

namespace ForgeDeck.Core.SourceControl;

public sealed class LocalRepositoryInspector
{
    private static readonly Regex GitHubHttps = new(@"^https://github\.com/(?<owner>[^/]+)/(?<name>[^/]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GitHubSsh = new(@"^git@github\.com:(?<owner>[^/]+)/(?<name>[^/]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public LocalRepositoryInfo Inspect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A local repository path is required.");
        }

        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Path '{fullPath}' does not exist.");
        }

        var root = TryGit(fullPath, "rev-parse", "--show-toplevel");
        if (root is null)
        {
            return new(fullPath, fullPath, false, null, null, 0, true, [], null, null, null);
        }

        root = Path.GetFullPath(root);
        var branch = TryGit(root, "rev-parse", "--abbrev-ref", "HEAD") ?? "HEAD";
        var head = TryGit(root, "rev-parse", "HEAD");
        var status = TryGit(root, "status", "--porcelain") ?? "";
        var modified = status.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
        var remotes = ParseRemotes(TryGit(root, "remote", "-v") ?? "");
        var origin = remotes.FirstOrDefault(remote => remote.Name.Equals("origin", StringComparison.OrdinalIgnoreCase))?.Url
            ?? remotes.FirstOrDefault()?.Url;
        var detected = DetectGitHub(origin);
        LocalRemoteAheadBehind? aheadBehind = null;
        if (!string.IsNullOrWhiteSpace(branch) && branch != "HEAD")
        {
            var counts = TryGit(root, "rev-list", "--left-right", "--count", $"@{{u}}...HEAD");
            if (counts is not null)
            {
                var parts = counts.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0], out var behind) && int.TryParse(parts[1], out var ahead))
                {
                    aheadBehind = new(ahead, behind);
                }
            }
        }

        return new(fullPath, root, true, branch, head, modified, modified == 0, remotes, origin, detected, aheadBehind);
    }

    public static DetectedGitHubRepository? DetectGitHub(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var match = GitHubHttps.Match(url.Trim()) is { Success: true } https ? https : GitHubSsh.Match(url.Trim());
        if (!match.Success)
        {
            return null;
        }

        var owner = match.Groups["owner"].Value;
        var name = match.Groups["name"].Value;
        return new(owner, name, $"https://github.com/{owner}/{name}");
    }

    private static IReadOnlyList<LocalRemoteInfo> ParseRemotes(string output)
    {
        var remotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            remotes.TryAdd(parts[0], parts[1]);
        }
        return remotes.Select(pair => new LocalRemoteInfo(pair.Key, pair.Value)).ToArray();
    }

    private static string? TryGit(string workingDirectory, params string[] args)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (var arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
        }
        catch (Exception) { return null; }
    }
}
