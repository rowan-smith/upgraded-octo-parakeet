namespace Modules.Pipelines;

public sealed class PipelinesOptions
{
    public const string SectionName = "Pipelines";
    public string ExecutionMode { get; set; } = "Runner"; // Runner | Simulated
    public int RunnerHeartbeatTimeoutSeconds { get; set; } = 30;
    public int RunnerDisconnectGraceSeconds { get; set; } = 60;
    public int MaxLogBytesPerStep { get; set; } = 2_000_000;
    public int MaxLogLineLength { get; set; } = 8_000;
    public string ArtifactRoot { get; set; } = "data/pipeline-artifacts";
    public string LogRoot { get; set; } = "data/pipeline-logs";
    public string WorkspaceRoot { get; set; } = "data/runner-work"; // for simulated only
}
