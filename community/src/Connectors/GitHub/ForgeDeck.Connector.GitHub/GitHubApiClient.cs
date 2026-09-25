using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ForgeDeck.Contracts.Integrations;
using ForgeDeck.Contracts.SourceControl;

namespace ForgeDeck.Connectors.GitHub;

public sealed class GitHubApiClient(IHttpClientFactory clients, IProviderCredentialStore credentials)
{
    public Task<JsonDocument> GetAsync(string path, CancellationToken token) => SendAsync(HttpMethod.Get, path, null, token);
    public Task<JsonDocument> PostAsync(string path, object body, CancellationToken token) => SendAsync(HttpMethod.Post, path, body, token);
    public Task<JsonDocument> PutAsync(string path, object body, CancellationToken token) => SendAsync(HttpMethod.Put, path, body, token);
    public Task<JsonDocument> PatchAsync(string path, object body, CancellationToken token) => SendAsync(HttpMethod.Patch, path, body, token);

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        var credential = credentials.Get("github");
        if (!string.IsNullOrWhiteSpace(credential))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response;
        try { response = await clients.CreateClient("github").SendAsync(request, token); }
        catch (HttpRequestException exception) { throw new SourceProviderException(SourceProviderErrorKind.Temporary, "GitHub is currently unavailable.", innerException: exception); }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                await ThrowProviderError(response, token);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            return await JsonDocument.ParseAsync(stream, cancellationToken: token);
        }
    }

    private static async Task ThrowProviderError(HttpResponseMessage response, CancellationToken token)
    {
        var message = await ReadMessage(response, token);
        var rateLimited = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) && values.FirstOrDefault() == "0";
        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => rateLimited ? SourceProviderErrorKind.RateLimited : SourceProviderErrorKind.Authentication,
            HttpStatusCode.NotFound => SourceProviderErrorKind.NotFound,
            HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity => SourceProviderErrorKind.Conflict,
            _ when (int)response.StatusCode >= 500 => SourceProviderErrorKind.Temporary,
            _ => SourceProviderErrorKind.RepositoryState
        };
        int? retry = response.Headers.RetryAfter?.Delta is { } delta ? (int)delta.TotalSeconds : null;
        throw new SourceProviderException(kind, $"GitHub: {message}", retry);
    }

    private static async Task<string> ReadMessage(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return document.RootElement.TryGetProperty("message", out var value) ? value.GetString() ?? "Request failed." : "Request failed.";
        }
        catch (JsonException) { return $"Request failed with status {(int)response.StatusCode}."; }
    }
}
