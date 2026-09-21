namespace Platform.Core.Identity;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "admin";

    /// <summary>When true (typical for Development), show the default-credentials warning.</summary>
    public bool IsDevelopmentDefault { get; set; } = true;
}
