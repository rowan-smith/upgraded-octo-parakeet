namespace ForgeDeck.Core.Identity;

/// <summary>
/// Singular installation default used for bootstrap and development seed.
/// Must be changed on first login (<see cref="Domain.UserAccount.MustChangePassword"/>).
/// </summary>
public static class DefaultInstallCredentials
{
    public const string Username = "admin";
    public const string Password = "admin";
    public const string Email = "admin@forgedeck.dev";
    public const string DisplayName = "Admin";
}
