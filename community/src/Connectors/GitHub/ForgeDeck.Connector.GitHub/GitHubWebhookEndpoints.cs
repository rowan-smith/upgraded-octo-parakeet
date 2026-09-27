using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Git.Contracts.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace ForgeDeck.Connectors.GitHub;

public static class GitHubWebhookEndpoints
{
    private static readonly ConcurrentDictionary<string, byte> SeenDeliveries = new(StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapGitHubWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/connectors/github/webhook", async (
            HttpRequest request,
            IEventPublisher events,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var secret = configuration["Connectors:GitHub:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                return Results.Problem("GitHub webhook secret is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var body = await reader.ReadToEndAsync(cancellationToken);
            request.Body.Position = 0;

            if (!request.Headers.TryGetValue("X-Hub-Signature-256", out var signatureHeader)
                || !VerifySignature(secret, body, signatureHeader.ToString()))
            {
                return Results.Unauthorized();
            }

            var deliveryId = request.Headers.TryGetValue("X-GitHub-Delivery", out var delivery)
                ? delivery.ToString()
                : Guid.NewGuid().ToString("N");

            if (!SeenDeliveries.TryAdd(deliveryId, 0))
            {
                return Results.Ok(new { deduplicated = true, deliveryId });
            }

            var eventName = request.Headers.TryGetValue("X-GitHub-Event", out var name) ? name.ToString() : "";
            if (!string.Equals(eventName, "push", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Ok(new { ignored = true, eventName });
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var repoFullName = root.GetProperty("repository").GetProperty("full_name").GetString() ?? "unknown/unknown";
            var branchRef = root.TryGetProperty("ref", out var refEl) ? refEl.GetString() ?? "refs/heads/main" : "refs/heads/main";
            var branch = branchRef.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? branchRef["refs/heads/".Length..]
                : branchRef;
            var commitSha = root.TryGetProperty("after", out var after) ? after.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(commitSha) || commitSha.All(c => c == '0'))
            {
                return Results.Ok(new { ignored = true, reason = "delete-or-empty" });
            }

            var repositoryId = CreateStableGuid(repoFullName);
            await events.PublishAsync(
                new GitRepositoryPushEvent(repositoryId, "ATL", repoFullName, branch, commitSha),
                new PublishOptions
                {
                    Actor = new EventActor(ActorType.External, "github", "GitHub"),
                    Publisher = "forgedeck.connector.github"
                },
                cancellationToken);

            return Results.Accepted(value: new { deliveryId, repository = repoFullName, branch, commitSha });
        });

        return app;
    }

    private static bool VerifySignature(string secret, string body, string signatureHeader)
    {
        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedHex = signatureHeader[prefix.Length..].Trim();
        var key = Encoding.UTF8.GetBytes(secret);
        var payload = Encoding.UTF8.GetBytes(body);
        var hash = HMACSHA256.HashData(key, payload);
        var actualHex = Convert.ToHexString(hash);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actualHex.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(expectedHex.ToLowerInvariant()));
    }

    private static Guid CreateStableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }
}
