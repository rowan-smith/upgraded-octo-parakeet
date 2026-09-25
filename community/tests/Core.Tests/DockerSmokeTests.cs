using ForgeDeck.Runner;
using Microsoft.Extensions.Logging.Abstractions;

namespace Core.Tests;

/// <summary>
/// Optional Docker availability smoke. Enable with FORGEDECK_DOCKER_SMOKE=1.
/// </summary>
public sealed class DockerSmokeTests
{
    [Fact]
    public async Task Docker_run_hello_world_when_enabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FORGEDECK_DOCKER_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var executor = new ProcessExecutor(NullLogger<ProcessExecutor>.Instance);
        var result = await executor.RunAsync(
            "docker run --rm hello-world",
            Directory.GetCurrentDirectory(),
            shell: "",
            new Dictionary<string, string>(),
            120,
            null,
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Hello from Docker", result.Stdout + result.Stderr, StringComparison.OrdinalIgnoreCase);
    }
}
