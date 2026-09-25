using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Review.Team;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Review.Enterprise;

/// <summary>
/// Enterprise Review delta. Declares advanced capabilities; implementations land as contributions over time.
/// Multi-approval lives in Review.Team — this module does not re-register it.
/// </summary>
public sealed class ReviewEnterpriseModule : IPlatformModule
{
    // Keep a hard compile-time dependency on Team (Enterprise is a delta on Team).
    private static readonly Type TeamAnchor = typeof(ReviewTeamModule);

    public ModuleManifest Manifest { get; } = new(
        "review-enterprise",
        "Review Enterprise",
        "0.1.0",
        "Enterprise",
        [
            KnownCapabilities.Review.PathPolicy,
            KnownCapabilities.Review.ConditionalPolicy,
            KnownCapabilities.Review.PolicyComposition,
            KnownCapabilities.Review.ReviewDismissal,
            KnownCapabilities.Review.SeparationOfDuties,
            KnownCapabilities.Review.ComplianceExport
        ],
        [],
        []);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        _ = TeamAnchor;
        // Enterprise policy contributors register here when implemented.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
