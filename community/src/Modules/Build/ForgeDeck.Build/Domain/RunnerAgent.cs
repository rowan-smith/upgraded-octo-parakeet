using System.Text.Json.Serialization;

namespace ForgeDeck.Build.Domain;

public sealed class RunnerAgent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string TokenHash { get; init; }
    public string? OperatingSystem { get; set; }
    public IReadOnlyList<string> Capabilities { get; set; } = [];
    public int Concurrency { get; set; } = 1;
    public int CurrentJobCount { get; set; }
    public string Version { get; set; } = "0.0.0";
    [JsonInclude] public RunnerStatus Status { get; private set; } = RunnerStatus.Offline;
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
    [JsonInclude] public DateTimeOffset? LastHeartbeatAt { get; private set; }
    public int? CpuCount { get; set; }
    public long? MemoryBytes { get; set; }

    public void Heartbeat(RunnerStatus status, IReadOnlyList<string> capabilities, int currentJobCount, string version, int? cpuCount, long? memoryBytes, string? operatingSystem)
    {
        Status = status;
        Capabilities = capabilities;
        CurrentJobCount = currentJobCount;
        Version = version;
        CpuCount = cpuCount;
        MemoryBytes = memoryBytes;
        if (!string.IsNullOrWhiteSpace(operatingSystem))
        {
            OperatingSystem = operatingSystem;
        }

        LastHeartbeatAt = DateTimeOffset.UtcNow;
    }

    public void MarkOffline() => Status = RunnerStatus.Offline;

    public bool HasCapacity => CurrentJobCount < Concurrency;

    public bool Supports(IReadOnlyList<string> requiredCapabilities) =>
        requiredCapabilities.Count == 0 ||
        requiredCapabilities.All(required => Capabilities.Any(c => c.Equals(required, StringComparison.OrdinalIgnoreCase)));
}

public sealed class RunnerRegistrationToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string TokenHash { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public string CreatedBy { get; init; } = "system";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsConsumed => ConsumedAt.HasValue;
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
