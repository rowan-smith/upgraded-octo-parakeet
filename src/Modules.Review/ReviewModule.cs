using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Review.Api;
using Modules.Review.Application;
using Modules.Review.Domain;
using Modules.Review.Infrastructure;
using Platform.Contracts.Events;
using Platform.Contracts.Modules;

namespace Modules.Review;

public sealed class ReviewModule : IPlatformModule
{
    public ModuleManifest Manifest { get; } = new(
        "review", "Review", "0.3.0", "Community", ["Review.BasicApproval"],
        [new("changes", "Pull Requests", "/changes", "Review", 10), new("queue", "Review queue", "/queue", "Review", 20)],
        [new("change", "overview", "Overview", "/api/review/changes/{id}", 10), new("change", "files", "Changes", "/api/review/changes/{id}", 20),
         new("change", "discussion", "Discussion", "/api/review/changes/{id}", 30), new("change", "reviewers", "Reviewers", "/api/review/changes/{id}", 40)]);
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ReviewOptions>(configuration.GetSection(ReviewOptions.SectionName));
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ReviewOptions>>().Value;
            var capabilities = provider.GetRequiredService<Platform.Contracts.Capabilities.ICapabilityService>();
            var context = provider.GetRequiredService<Platform.Core.Context.PlatformContextStore>();
            var minimum = Math.Max(1, options.MinimumApprovals);
            if (minimum > 1 && !capabilities.Has(context.Organisation.Id, Platform.Contracts.Capabilities.KnownCapabilities.Review.MultiApproval))
                minimum = 1;
            return new ReviewPolicyState { MinimumApprovals = minimum };
        });
        services.AddSingleton<ApprovalPolicyResolver>();
        services.AddSingleton<ReviewSchemaInitializer>();
        services.AddSingleton<IChangeRepository, SqliteChangeRepository>();
        services.AddSingleton<ReviewService>();
        services.AddSingleton<CheckQueryService>();
        services.AddSingleton<PipelineActivityHandler>();
        services.AddSingleton<Platform.Contracts.Onboarding.IOnboardingContributor, ReviewOnboardingContributor>();
        services.AddSingleton<IEventHandler<PipelineRunStarted>>(sp => sp.GetRequiredService<PipelineActivityHandler>());
        services.AddSingleton<IEventHandler<PipelineRunCompleted>>(sp => sp.GetRequiredService<PipelineActivityHandler>());
        services.AddHostedService<ChangeRefreshHostedService>();
    }
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapReviewEndpoints();
}
