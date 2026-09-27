namespace ForgeDeck.Deploy;

public sealed class DeployOptions
{
    public const string SectionName = "Deploy";

    /// <summary>Immediate (default, sync success) or Agent (queued for deploy agents).</summary>
    public string ExecutionMode { get; set; } = "Immediate";
}
