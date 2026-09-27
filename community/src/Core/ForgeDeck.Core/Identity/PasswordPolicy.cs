namespace ForgeDeck.Core.Identity;

public static class PasswordPolicy
{
    public static void Validate(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10)
            throw new ArgumentException("Password must contain at least 10 characters.");
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new ArgumentException("Password must include letters and digits.");
    }
}
