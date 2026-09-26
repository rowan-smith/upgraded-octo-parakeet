using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Onboarding;
using ForgeDeck.Contracts.Search;
using ForgeDeck.Contracts.Services;
using ForgeDeck.Core.Context;
using ForgeDeck.Review.Api;
using ForgeDeck.Review.Application;
using ForgeDeck.Review.Domain;
using ForgeDeck.Review.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ForgeDeck.Review;

public sealed class ReviewModule : IPlatformModule
{
    public ModuleManifest Manifest { get; } = new(
        "review", "Review", "0.3.0", "Community", ["Review.BasicApproval"],
        [new("changes", "Pull Requests", "/changes", "Review", 210), new("queue", "Review queue", "/queue", "Review", 220)],
        [new("change", "overview", "Overview", "/api/review/changes/{id}", 10), new("change", "files", "Changes", "/api/review/changes/{id}", 20),
         new("change", "discussion", "Discussion", "/api/review/changes/{id}", 30), new("change", "reviewers", "Reviewers", "/api/review/changes/{id}", 40)],
        Permissions:
        [
            PlatformPermissions.ProjectRead,
            PlatformPermissions.RepositoryRead,
            PlatformPermissions.ReviewRead,
            PlatformPermissions.ReviewCreate,
            PlatformPermissions.ReviewStatusWrite,
            PlatformPermissions.BuildRead
        ],
        Publishes: ["review.created", "review.updated", "review.approved", "review.merged"],
        Subscribes: ["build.started", "build.completed", "build.failed"],
        Provides: ["review"],
        ExtensionPointContributions:
        [
            ExtensionPoints.ReviewTabs,
            ExtensionPoints.ReviewChecks,
            ExtensionPoints.ReviewMergeGates,
            ExtensionPoints.PlatformNavigation
        ]);
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ReviewOptions>(configuration.GetSection(ReviewOptions.SectionName));
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ReviewOptions>>().Value;
            var capabilities = provider.GetRequiredService<ICapabilityService>();
            var context = provider.GetRequiredService<PlatformContextStore>();
            var minimum = Math.Max(1, options.MinimumApprovals);
            if (minimum > 1 && !capabilities.Has(context.Organisation.Id, KnownCapabilities.Review.MultiApproval))
            {
                minimum = 1;
            }

            return new ReviewPolicyState { MinimumApprovals = minimum };
        });
        services.AddSingleton<ApprovalPolicyResolver>();
        services.AddSingleton<ReviewSchemaInitializer>();
        services.AddSingleton<IChangeRepository, SqliteChangeRepository>();
        services.AddSingleton<ReviewService>();
        services.AddSingleton<ReviewDomainService>();
        services.AddSingleton<IReviewService>(sp => sp.GetRequiredService<ReviewDomainService>());
        services.AddSingleton<CheckQueryService>();
        services.AddSingleton<PipelineActivityHandler>();
        services.AddSingleton<IOnboardingContributor, ReviewOnboardingContributor>();
        services.AddSingleton<ISearchContributor, ReviewSearchContributor>();
        services.AddSingleton<IEventHandler<PipelineRunStarted>>(sp => sp.GetRequiredService<PipelineActivityHandler>());
        services.AddSingleton<IEventHandler<PipelineRunCompleted>>(sp => sp.GetRequiredService<PipelineActivityHandler>());
        services.AddHostedService<ChangeRefreshHostedService>();
    }
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapReviewEndpoints();
}
