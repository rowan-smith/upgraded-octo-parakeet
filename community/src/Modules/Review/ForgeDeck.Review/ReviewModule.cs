using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Checks;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Search;
using ForgeDeck.Contracts.Services;
using ForgeDeck.Core.Context;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Git.Contracts.Events;
using ForgeDeck.Messaging;
using ForgeDeck.Review.Api;
using ForgeDeck.Review.Application;
using ForgeDeck.Review.Contracts.Events;
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
        Publishes: ReviewEventContracts.All().Select(c => c.Type).ToArray(),
        Subscribes:
        [
            GitEventContracts.RepositoryPush.Type,
            BuildEventContracts.PipelineRunStarted.Type,
            BuildEventContracts.PipelineRunSucceeded.Type,
            BuildEventContracts.PipelineRunFailed.Type,
            BuildEventContracts.PipelineRunCancelled.Type,
            CheckUpdatedEventContract.Type
        ],
        Provides: ["review"],
        ExtensionPointContributions:
        [
            ExtensionPoints.ReviewTabs,
            ExtensionPoints.ReviewChecks,
            ExtensionPoints.ReviewMergeGates,
            ExtensionPoints.PlatformNavigation
        ],
        ApiRoutePrefixes: ["/api/review"]);

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
        var connectionString = configuration.GetConnectionString("Review")
            ?? configuration.GetConnectionString("Platform")
            ?? "Data Source=data/forgedeck.db";
        services.AddDbContextFactory<ReviewDbContext>(options =>
            DatabaseProvider.Configure(options, configuration, connectionString));
        services.AddSingleton<IChangeRepository, EfChangeRepository>();
        services.AddSingleton<ReviewService>();
        services.AddSingleton<ReviewDomainService>();
        services.AddSingleton<IReviewService>(sp => sp.GetRequiredService<ReviewDomainService>());
        services.AddSingleton<CheckQueryService>();
        services.AddSingleton<ISearchContributor, ReviewSearchContributor>();
        services.AddSingleton(new EventContractRegistration(ReviewEventContracts.All().ToArray()));
        services.AddEventHandler<BuildPipelineRunStartedEvent, PipelineActivityHandler>("forgedeck.review.pipeline-started");
        services.AddEventHandler<BuildPipelineRunSucceededEvent, PipelineSucceededActivityHandler>("forgedeck.review.pipeline-succeeded");
        services.AddEventHandler<BuildPipelineRunFailedEvent, PipelineFailedActivityHandler>("forgedeck.review.pipeline-failed");
        services.AddEventHandler<BuildPipelineRunCancelledEvent, PipelineCancelledActivityHandler>("forgedeck.review.pipeline-cancelled");
        services.AddEventHandler<CheckUpdatedEvent, CheckProjectionHandler>("forgedeck.review.check-projection");
        services.AddEventHandler<GitRepositoryPushEvent, ReviewPushHandler>("forgedeck.review.git-push");
        services.AddHostedService<ChangeRefreshHostedService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapReviewEndpoints();
}
