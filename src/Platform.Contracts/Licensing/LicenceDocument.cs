namespace Platform.Contracts.Licensing;

public sealed class LicenceDocument
{
    public Guid OrganisationId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public Dictionary<string, LicenceModuleDocument> Modules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Signature { get; set; }
}

public sealed class LicenceModuleDocument
{
    public string Edition { get; set; } = "Commercial";
    public List<string> Capabilities { get; set; } = [];
    public DateTimeOffset? ExpiresAt { get; set; }
}
