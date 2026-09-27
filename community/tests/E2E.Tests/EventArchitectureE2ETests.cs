using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Git.Contracts.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace E2E.Tests;

[Trait("Category", "E2E")]
public sealed class EventArchitectureE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EventArchitectureE2ETests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Safe_mode_starts_without_module_handlers()
    {
        await using var host = CreateHost(configure: builder =>
        {
            builder.UseSetting("SafeMode", "true");
        });

        using var client = AuthenticatedClient(host);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);

        var modules = await client.GetStringAsync("/api/platform/modules");
        Assert.DoesNotContain("\"id\":\"review\"", modules);
        Assert.DoesNotContain("\"id\":\"pipelines\"", modules);
        Assert.DoesNotContain("\"id\":\"deploy\"", modules);

        var subscriptions = await client.GetAsync("/api/platform/events/subscriptions");
        Assert.Equal(HttpStatusCode.OK, subscriptions.StatusCode);
        var body = await subscriptions.Content.ReadAsStringAsync();
        Assert.True(
            body is "[]" or "null" || body.Trim() == "[]",
            $"Expected no module event subscriptions in safe mode, got: {body}");
    }

    [Fact]
    public async Task Event_diagnostics_endpoints_exist()
    {
        await using var host = CreateHost();
        using var client = AuthenticatedClient(host);

        string[] routes =
        [
            "/api/platform/events",
            "/api/platform/events/outbox",
            "/api/platform/events/failures",
            "/api/platform/events/subscriptions",
            "/api/platform/diagnostics"
        ];

        foreach (var route in routes)
        {
            var response = await client.GetAsync(route);
            Assert.True(response.IsSuccessStatusCode, $"{route} => {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Publishing_git_push_without_build_handlers_does_not_throw()
    {
        await using var host = CreateHost(configure: builder =>
        {
            builder.UseSetting("Modules:build:Enabled", "false");
        });

        var publisher = host.Services.GetRequiredService<IEventPublisher>();
        var subscriber = host.Services.GetRequiredService<IEventSubscriber>();

        Assert.DoesNotContain(
            subscriber.ListSubscriptions(),
            s => s.ConsumerId.Contains("build", StringComparison.OrdinalIgnoreCase));

        var exception = await Record.ExceptionAsync(() =>
            publisher.PublishAsync(new GitRepositoryPushEvent(
                Guid.NewGuid(),
                "ATL",
                "org/repo",
                "main",
                "abc1234")));

        Assert.Null(exception);

        using var client = AuthenticatedClient(host);
        var outbox = await client.GetAsync("/api/platform/events/outbox");
        Assert.Equal(HttpStatusCode.OK, outbox.StatusCode);
        var body = await outbox.Content.ReadAsStringAsync();
        Assert.Contains("forgedeck.git.repository-push", body);
    }

    private WebApplicationFactory<Program> CreateHost(Action<IWebHostBuilder>? configure = null)
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-e2e-events-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-e2e-events-keys-{Guid.NewGuid():N}");
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={database}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Core:SeedDemoOnEmpty", "true");
            builder.UseSetting("Pipelines:ExecutionMode", "Simulated");
            configure?.Invoke(builder);
        });
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        var response = client.PostAsJsonAsync("/api/auth/login", new { email = "maya@forgedeck.dev", password = "demo" }).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        var token = doc.RootElement.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
