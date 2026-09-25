namespace ForgeDeck.Contracts.Licensing;

/// <summary>Air-gapped licence challenge produced on the installation (licence-request.nlr).</summary>
public sealed class LicenceRequestDocument
{
    public int Version { get; set; } = 1;
    public Guid InstanceId { get; set; }
    public string InstallationVersion { get; set; } = "0.0.0";
    public Guid? OrganisationId { get; set; }
    public string? OrganisationName { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Signed usage export for offline seat/capacity reconciliation (usage-*.nusage).</summary>
public sealed class UsageExportDocument
{
    public int Version { get; set; } = 1;
    public string? LicenceId { get; set; }
    public Guid InstanceId { get; set; }
    public string Period { get; set; } = "";
    public int PeakUsers { get; set; }
    public int ActiveUsers { get; set; }
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Signature { get; set; }
}
