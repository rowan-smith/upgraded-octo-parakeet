using ForgeDeck.Runner;
using Microsoft.Extensions.Logging.Abstractions;

namespace Core.Tests;

public sealed class ProcessExecutorTests
{
    [Fact]
    public async Task Successful_command_captures_stdout_and_exit_zero()
    {
        var executor = new ProcessExecutor(NullLogger<ProcessExecutor>.Instance);
        var (shell, command) = WindowsOrUnix(
            ("cmd", "echo hello-runner"),
            ("bash", "echo hello-runner"));
        var result = await executor.RunAsync(
            command,
            Directory.GetCurrentDirectory(),
            shell,
            new Dictionary<string, string>(),
            30,
            null,
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello-runner", result.Stdout);
    }

    [Fact]
    public async Task Failing_command_returns_non_zero_exit()
    {
        var executor = new ProcessExecutor(NullLogger<ProcessExecutor>.Instance);
        var (shell, command) = WindowsOrUnix(("cmd", "exit /b 7"), ("bash", "exit 7"));
        var result = await executor.RunAsync(
            command,
            Directory.GetCurrentDirectory(),
            shell,
            new Dictionary<string, string>(),
            30,
            null,
            CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Cancellation_kills_long_running_process()
    {
        var executor = new ProcessExecutor(NullLogger<ProcessExecutor>.Instance);
        var (shell, command) = WindowsOrUnix(("cmd", "ping -n 30 127.0.0.1 >nul"), ("bash", "sleep 30"));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.RunAsync(
            command,
            Directory.GetCurrentDirectory(),
            shell,
            new Dictionary<string, string>(),
            60,
            null,
            cts.Token));
    }

    private static (string Shell, string Command) WindowsOrUnix((string Shell, string Command) windows, (string Shell, string Command) unix) =>
        OperatingSystem.IsWindows() ? windows : unix;
}
