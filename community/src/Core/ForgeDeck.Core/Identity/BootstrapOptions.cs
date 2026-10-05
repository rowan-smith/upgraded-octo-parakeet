namespace ForgeDeck.Core.Identity;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string Username { get; set; } = DefaultInstallCredentials.Username;
    public string Password { get; set; } = DefaultInstallCredentials.Password;

    /// <summary>When true (typical for Development), show the default-credentials warning.</summary>
    public bool IsDevelopmentDefault { get; set; } = true;

    public bool UsesDevelopmentDefaults =>
        IsDevelopmentDefault
        && string.Equals(EffectiveUsername, DefaultInstallCredentials.Username, StringComparison.OrdinalIgnoreCase)
        && EffectivePassword == DefaultInstallCredentials.Password;

    /// <summary>
    /// When <see cref="IsDevelopmentDefault"/> is true, empty username/password fall back to
    /// <see cref="DefaultInstallCredentials"/> so appsettings need not repeat them.
    /// </summary>
    public string EffectiveUsername =>
        IsDevelopmentDefault && string.IsNullOrWhiteSpace(Username)
            ? DefaultInstallCredentials.Username
            : Username;

    public string EffectivePassword =>
        IsDevelopmentDefault && string.IsNullOrEmpty(Password)
            ? DefaultInstallCredentials.Password
            : Password;
}
