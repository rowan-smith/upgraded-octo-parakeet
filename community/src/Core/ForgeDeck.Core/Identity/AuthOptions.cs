namespace ForgeDeck.Core.Identity;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// When true (default), passwords must be ≥10 characters with letters and digits.
    /// Development typically sets this false so easy local passwords are allowed.
    /// </summary>
    public bool RequireStrongPasswords { get; set; } = true;
}
