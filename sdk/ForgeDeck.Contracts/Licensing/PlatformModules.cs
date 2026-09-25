namespace ForgeDeck.Contracts.Licensing;

/// <summary>Canonical platform module ids used in licence documents and entitlement lookups.</summary>
public static class PlatformModules
{
    public const string Git = "git";
    public const string Code = "code";
    public const string Review = "review";
    public const string Build = "build";
    public const string Deploy = "deploy";

    public static IReadOnlyList<string> All { get; } =
        [Git, Code, Review, Build, Deploy];

    public static string Normalize(string moduleId)
    {
        foreach (var suffix in new[] { "-commercial", "-enterprise", "-team" })
        {
            if (moduleId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return moduleId[..^suffix.Length].ToLowerInvariant();
            }
        }

        // Host runtime id "pipelines" is the Build module.
        if (moduleId.Equals("pipelines", StringComparison.OrdinalIgnoreCase))
        {
            return Build;
        }

        return moduleId.ToLowerInvariant();
    }
}
