using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace E2E.Playwright.Tests;

[CollectionDefinition("playwright")]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightBrowserFixture>;

/// <summary>Process-wide Playwright browser (Chromium).</summary>
public sealed class PlaywrightBrowserFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Microsoft.Playwright.Program.Main(["install", "chromium"]);
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        Playwright?.Dispose();
    }
}

/// <summary>Isolated Kestrel-hosted ForgeDeck instance for one test (or class).</summary>
public sealed class ForgeDeckHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _databasePath;

    private ForgeDeckHost(WebApplicationFactory<Program> factory, string databasePath, Uri baseAddress, HttpClient api)
    {
        _factory = factory;
        _databasePath = databasePath;
        BaseAddress = baseAddress;
        Api = api;
    }

    public Uri BaseAddress { get; }
    public HttpClient Api { get; }

    public static ForgeDeckHost StartEmpty() => Start(
        databasePrefix: "forgedeck-pw",
        environment: "Testing",
        seedDemo: false);

    public static ForgeDeckHost StartSeeded() => Start(
        databasePrefix: "forgedeck-pw-seeded",
        environment: "Development",
        seedDemo: true);

    /// <summary>Empty install that verifies licences with the provided public key (pair with TestLicenceFactory).</summary>
    public static ForgeDeckHost StartEmptyWithLicenceKey(string publicKeyPem) => Start(
        databasePrefix: "forgedeck-pw-lic",
        environment: "Testing",
        seedDemo: false,
        publicKeyPem: publicKeyPem);

    private static ForgeDeckHost Start(string databasePrefix, string environment, bool seedDemo, string? publicKeyPem = null)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{databasePrefix}-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"{databasePrefix}-keys-{Guid.NewGuid():N}");
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={databasePath}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Core:SeedDemoOnEmpty", seedDemo ? "true" : "false");
            builder.UseSetting("Pipelines:ExecutionMode", "Simulated");
            if (!string.IsNullOrWhiteSpace(publicKeyPem))
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Licensing:PublicKeyPem"] = publicKeyPem
                    });
                });
            }
        });
        factory.UseKestrel(0);
        factory.StartServer();

        var baseAddress = ResolveBaseAddress(factory);
        return new ForgeDeckHost(factory, databasePath, baseAddress, factory.CreateClient());
    }

    private static Uri ResolveBaseAddress(WebApplicationFactory<Program> factory)
    {
        var server = factory.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is { Count: > 0 })
        {
            return new Uri(addresses.Last().Replace("://+", "://127.0.0.1", StringComparison.Ordinal)
                .Replace("://[*]", "://127.0.0.1", StringComparison.Ordinal));
        }

        using var probe = factory.CreateClient();
        return factory.ClientOptions.BaseAddress
               ?? probe.BaseAddress
               ?? throw new InvalidOperationException("Kestrel did not expose a base address.");
    }

    public async Task<PageSession> NewPageAsync(IBrowser browser, bool clearStorage = true)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseAddress.ToString().TrimEnd('/'),
            ViewportSize = new ViewportSize { Width = 1440, Height = 1200 }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        if (clearStorage)
        {
            await page.EvaluateAsync("() => localStorage.clear()");
            await page.GotoAsync("/");
        }
        await page.WaitForFunctionAsync("() => document.getElementById('app')?.getAttribute('aria-busy') === 'false'");
        return new PageSession(context, page);
    }

    public async ValueTask DisposeAsync()
    {
        Api.Dispose();
        await _factory.DisposeAsync();
        if (File.Exists(_databasePath))
        {
            try { File.Delete(_databasePath); } catch { /* ignore locked db */ }
        }
    }
}

public sealed class PageSession(IBrowserContext context, IPage page) : IAsyncDisposable
{
    public IPage Page => page;

    public async ValueTask DisposeAsync()
    {
        await context.CloseAsync();
    }
}
