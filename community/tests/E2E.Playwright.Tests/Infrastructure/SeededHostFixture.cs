namespace E2E.Playwright.Tests;

/// <summary>One seeded ForgeDeck host shared by a test class to keep large journey matrices fast.</summary>
public sealed class SeededHostFixture : IAsyncLifetime
{
    public ForgeDeckHost Host { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Host = ForgeDeckHost.StartSeeded();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Host is not null)
        {
            await Host.DisposeAsync();
        }
    }
}
