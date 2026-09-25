using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Review.Domain;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Review.Team;

public sealed class ReviewTeamModule : IPlatformModule
{
    public ModuleManifest Manifest { get; } = new(
        "review-team",
        "Review Team",
        "0.1.0",
        "Team",
        [
            KnownCapabilities.Review.MultiApproval,
            KnownCapabilities.Review.TeamApproval,
            KnownCapabilities.Review.CodeOwners
        ],
        [],
        []);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IApprovalPolicyFactory, MultiApprovalPolicyFactory>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
