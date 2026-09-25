using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ForgeDeck.Runner;

internal sealed class RunnerOptions
{
    public required string PlatformUrl { get; init; }
    public string? RunnerToken { get; init; }
    public string? RegistrationToken { get; init; }
    public required string RunnerName { get; init; }
    public required string WorkRoot { get; init; }
    public required string CredentialsPath { get; init; }
    public bool KeepFailedWorkspace { get; init; }
    public string Version { get; } = "0.1.0";

    public static RunnerOptions Parse(string[] args, IConfiguration configuration)
    {
        var cli = ParseArgs(args);
        string Resolve(string key, string? fallback = null) =>
            cli.GetValueOrDefault(key)
            ?? Environment.GetEnvironmentVariable(key.ToUpperInvariant().Replace('-', '_'))
            ?? configuration[key]
            ?? configuration[key.Replace('-', '_')]
            ?? fallback
            ?? string.Empty;

        var platformUrl = Resolve("url", null);
        if (string.IsNullOrWhiteSpace(platformUrl))
        {
            platformUrl = Resolve("PLATFORM_URL", "http://localhost:5262");
        }

        var workRoot = Resolve("work-root", null);
        if (string.IsNullOrWhiteSpace(workRoot))
        {
            workRoot = Resolve("WORK_ROOT", ".runner/work");
        }

        var name = Resolve("name", null);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Resolve("RUNNER_NAME", Environment.MachineName);
        }

        var runnerToken = FirstNonEmpty(Resolve("runner-token", null), Resolve("RUNNER_TOKEN", null));
        var registrationToken = FirstNonEmpty(Resolve("token", null), Resolve("REGISTRATION_TOKEN", null));
        var keepFailed = bool.TryParse(Resolve("KEEP_FAILED_WORKSPACE", "false"), out var keep) && keep;

        return new RunnerOptions
        {
            PlatformUrl = platformUrl.TrimEnd('/'),
            RunnerToken = string.IsNullOrWhiteSpace(runnerToken) ? null : runnerToken,
            RegistrationToken = string.IsNullOrWhiteSpace(registrationToken) ? null : registrationToken,
            RunnerName = name,
            WorkRoot = Path.GetFullPath(workRoot),
            CredentialsPath = Path.GetFullPath(Path.Combine(".runner", "credentials.json")),
            KeepFailedWorkspace = keepFailed
        };
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            map[key] = value;
        }
        return map;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

internal sealed record RunnerCredentials(Guid RunnerId, string Token);

internal static class CapabilityProbe
{
    public static async Task<IReadOnlyList<string>> DetectAsync(CancellationToken cancellationToken)
    {
        var caps = new List<string> { "dotnet", OperatingSystem.IsWindows() ? "windows" : "linux" };
        if (OperatingSystem.IsWindows() || File.Exists("/usr/bin/pwsh") || File.Exists("/usr/local/bin/pwsh"))
        {
            caps.Add("pwsh");
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is not null)
            {
                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode == 0)
                {
                    caps.Add("docker");
                }
            }
        }
        catch
        {
            // docker unavailable
        }

        return caps;
    }
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
