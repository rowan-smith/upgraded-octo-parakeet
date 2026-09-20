using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Review.Domain;
using Platform.Contracts.Capabilities;
using Platform.Contracts.Modules;

namespace Modules.Review.Commercial;

public sealed class ReviewCommercialModule : IPlatformModule
{
    public ModuleManifest Manifest { get; } = new(
        "review-commercial",
        "Review Commercial",
        "0.1.0",
        "Commercial",
        [
            KnownCapabilities.Review.MultiApproval,
            KnownCapabilities.Review.TeamApproval,
            KnownCapabilities.Review.CodeOwners,
            KnownCapabilities.Review.PathPolicy,
            KnownCapabilities.Review.ConditionalPolicy,
            KnownCapabilities.Review.PolicyComposition,
            KnownCapabilities.Review.ReviewDismissal
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
