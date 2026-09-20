using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PipelineRunner;

internal sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

internal sealed class ProcessExecutor(ILogger<ProcessExecutor> logger)
{
    public async Task<ProcessResult> RunAsync(
        string command,
        string workingDirectory,
        string shell,
        IReadOnlyDictionary<string, string> environment,
        int timeoutSeconds,
        Func<string, string, Task>? onOutput,
        CancellationToken cancellationToken)
    {
        var (fileName, arguments) = ResolveShell(shell, command);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var pair in environment)
            psi.Environment[pair.Key] = pair.Value;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var stdoutTask = PumpAsync(process.StandardOutput, "stdout", stdout, onOutput, cancellationToken);
        var stderrTask = PumpAsync(process.StandardError, "stderr", stderr, onOutput, cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        await Task.WhenAll(stdoutTask, stderrTask);
        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static (string FileName, string Arguments) ResolveShell(string shell, string command)
    {
        var requested = string.IsNullOrWhiteSpace(shell) ? null : shell.Trim().ToLowerInvariant();
        if (requested is "cmd" && OperatingSystem.IsWindows())
            return ("cmd.exe", $"/c {command}");
        if (requested is "bash" || (!OperatingSystem.IsWindows() && requested is null or "sh"))
            return ("bash", $"-lc {EscapeBash(command)}");
        if (requested is "pwsh" or "powershell" || (requested is null && OperatingSystem.IsWindows()))
        {
            if (FindOnPath("pwsh") is { } pwsh)
                return (pwsh, $"-NoLogo -NoProfile -Command {EscapePowerShell(command)}");
            if (FindOnPath("powershell") is { } windowsPowerShell)
                return (windowsPowerShell, $"-NoLogo -NoProfile -Command {EscapePowerShell(command)}");
            return ("cmd.exe", $"/c {command}");
        }

        return ("bash", $"-lc {EscapeBash(command)}");
    }

    private static string? FindOnPath(string fileName)
    {
        if (File.Exists(fileName)) return fileName;
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", "" } : [""];
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in paths)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(path, fileName + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static string EscapePowerShell(string command) =>
        $"\"{command.Replace("\"", "`\"")}\"";

    private static string EscapeBash(string command) =>
        $"'{command.Replace("'", "'\\''")}'";

    private static async Task PumpAsync(
        StreamReader reader,
        string stream,
        StringBuilder buffer,
        Func<string, string, Task>? onOutput,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            buffer.AppendLine(line);
            if (onOutput is not null)
                await onOutput(stream, line);
        }
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                logger.LogWarning("Killed process tree for PID {Pid}", process.Id);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to kill process");
        }
    }
}
