namespace ForgeDeck.Core.Identity;

public static class PasswordPolicy
{
    private static AuthOptions _options = new();

    public static void Configure(AuthOptions? options) =>
        _options = options ?? new AuthOptions();

    public static bool RequireStrongPasswords => _options.RequireStrongPasswords;

    public static void Validate(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.");
        }

        if (!_options.RequireStrongPasswords)
        {
            return;
        }

        if (password.Length < 10)
        {
            throw new ArgumentException("Password must contain at least 10 characters.");
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw new ArgumentException("Password must include letters and digits.");
        }
    }
}
