using ForgeDeck.Contracts.SourceControl;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Connectors.GitHub;

public static class GitHubConnectorRegistration
{
    public static IServiceCollection AddGitHubConnector(this IServiceCollection services)
    {
        services.AddHttpClient("github", client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ForgeDeck/0.3");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddSingleton<GitHubApiClient>();
        services.AddSingleton<GitHubSourceProvider>();
        services.AddSingleton<ISourceProvider>(provider => provider.GetRequiredService<GitHubSourceProvider>());
        services.AddSingleton<IChangeSourceProvider>(provider => provider.GetRequiredService<GitHubSourceProvider>());
        return services;
    }
}
