using System.Text;
using System.Text.Json;
using Platform.Contracts.SourceControl;

namespace Connectors.GitHub;

internal static class GitHubMapping
{
    public static SourceRepository Repository(JsonElement value) => new(
        "github", value.GetProperty("owner").GetProperty("login").GetString()!, value.GetProperty("name").GetString()!,
        value.GetProperty("default_branch").GetString()!, value.GetProperty("html_url").GetString(), value.GetProperty("private").GetBoolean());

    public static SourceCommit Commit(JsonElement value, string reference)
    {
        var commit = value.GetProperty("commit");
        var author = commit.GetProperty("author");
        return new(value.GetProperty("sha").GetString()!, commit.GetProperty("message").GetString()!,
            author.GetProperty("name").GetString()!, author.GetProperty("date").GetDateTimeOffset(), reference);
    }

    public static SourceDiffFile DiffFile(JsonElement value) => new(
        value.GetProperty("filename").GetString()!, value.TryGetProperty("patch", out var patch) ? patch.GetString() ?? "Binary file changed" : "Binary file changed",
        value.GetProperty("additions").GetInt32(), value.GetProperty("deletions").GetInt32(), value.GetProperty("status").GetString() ?? "modified",
        value.TryGetProperty("previous_filename", out var previous) ? previous.GetString() : null);

    public static ExternalChange Change(JsonElement value, SourceDiff? diff = null)
    {
        var mergedAt = Date(value, "merged_at");
        var state = value.GetProperty("state").GetString() == "closed" ? mergedAt is null ? "Closed" : "Merged"
            : value.GetProperty("draft").GetBoolean() ? "Draft" : "Open";
        return new(
            value.GetProperty("number").GetInt32().ToString(), value.GetProperty("number").GetInt32(), value.GetProperty("html_url").GetString()!,
            value.GetProperty("title").GetString()!, value.GetProperty("body").GetString() ?? "", value.GetProperty("user").GetProperty("login").GetString()!,
            value.GetProperty("head").GetProperty("ref").GetString()!, value.GetProperty("base").GetProperty("ref").GetString()!,
            value.GetProperty("head").GetProperty("sha").GetString()!, value.GetProperty("base").GetProperty("sha").GetString()!, state,
            value.GetProperty("draft").GetBoolean(), !value.TryGetProperty("mergeable", out var mergeable) || mergeable.ValueKind == JsonValueKind.Null || mergeable.GetBoolean(),
            value.GetProperty("created_at").GetDateTimeOffset(), value.GetProperty("updated_at").GetDateTimeOffset(), mergedAt, Date(value, "closed_at"), diff);
    }

    public static string? DecodeContent(JsonElement value)
    {
        if (!value.TryGetProperty("content", out var content) || value.GetProperty("encoding").GetString() != "base64") return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(content.GetString()!.Replace("\n", ""))); }
        catch (FormatException) { return null; }
    }

    private static DateTimeOffset? Date(JsonElement value, string name) =>
        value.TryGetProperty(name, out var date) && date.ValueKind != JsonValueKind.Null ? date.GetDateTimeOffset() : null;
}
