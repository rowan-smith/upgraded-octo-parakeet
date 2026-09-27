namespace ForgeDeck.Core.Identity;

/// <summary>
/// Maps legacy pipelines.* permission keys to canonical build.* (and the reverse for checks).
/// Existing RBAC assignments remain valid during the transition.
/// </summary>
public static class PermissionAliases
{
    private static readonly Dictionary<string, string> Canonical = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pipelines.read"] = "build.read",
        ["pipelines.run"] = "build.run",
        ["pipelines.cancel"] = "build.cancel",
        ["pipelines.manage"] = "build.manage",
        ["pipelines.runner.read"] = "build.runner.read",
        ["pipelines.runner.manage"] = "build.runner.manage"
    };

    private static readonly Dictionary<string, string> Legacy = Canonical
        .ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToCanonical(string permission) =>
        Canonical.TryGetValue(permission, out var mapped) ? mapped : permission;

    /// <summary>Returns the permission plus any alias that should satisfy the same check.</summary>
    public static IEnumerable<string> Expand(string permission)
    {
        yield return permission;
        var canonical = ToCanonical(permission);
        if (!canonical.Equals(permission, StringComparison.OrdinalIgnoreCase))
        {
            yield return canonical;
        }

        if (Legacy.TryGetValue(permission, out var legacy))
        {
            yield return legacy;
        }

        if (Legacy.TryGetValue(canonical, out var legacyFromCanonical) &&
            !legacyFromCanonical.Equals(permission, StringComparison.OrdinalIgnoreCase))
        {
            yield return legacyFromCanonical;
        }
    }
}
