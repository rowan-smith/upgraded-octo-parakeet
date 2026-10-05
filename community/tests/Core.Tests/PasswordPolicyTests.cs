using ForgeDeck.Core.Identity;

namespace Core.Tests;

public sealed class PasswordPolicyTests
{
    public PasswordPolicyTests() => PasswordPolicy.Configure(new AuthOptions { RequireStrongPasswords = true });

    [Fact]
    public void Strong_policy_rejects_short_and_simple_passwords()
    {
        PasswordPolicy.Configure(new AuthOptions { RequireStrongPasswords = true });
        Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate("admin"));
        Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate("abcdefghij"));
        PasswordPolicy.Validate("password123");
    }

    [Fact]
    public void Relaxed_policy_allows_easy_passwords()
    {
        PasswordPolicy.Configure(new AuthOptions { RequireStrongPasswords = false });
        PasswordPolicy.Validate("admin");
        PasswordPolicy.Validate("x");
        Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate(""));
        Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate("   "));
    }
}
