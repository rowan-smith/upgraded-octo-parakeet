using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ForgeDeck.Core.Identity;

namespace E2E.Tests;

/// <summary>Logs into the development-seeded owner and completes the forced password change when needed.</summary>
internal static class SeededAuth
{
    public const string WorkingPassword = "password123";

    public static async Task<string> LoginAsync(HttpClient client)
    {
        var token = await TryLoginAsync(client, WorkingPassword);
        if (token is not null)
        {
            return token;
        }

        token = await TryLoginAsync(client, DefaultInstallCredentials.Password)
                ?? throw new InvalidOperationException("Seeded login failed.");

        using var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new
            {
                currentPassword = DefaultInstallCredentials.Password,
                newPassword = WorkingPassword
            })
        };
        change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(change);
        response.EnsureSuccessStatusCode();
        return token;
    }

    public static async Task AuthenticateClientAsync(HttpClient client)
    {
        var token = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<string?> TryLoginAsync(HttpClient client, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = DefaultInstallCredentials.Email,
            password
        });
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString();
    }
}
