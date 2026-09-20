using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Review.Api;
using Modules.Review.Application;
using Modules.Review.Infrastructure;
using Platform.Contracts.Modules;

namespace Modules.Review;

public sealed class ReviewModule : IPlatformModule
{
    public ModuleManifest Manifest { get; } = new(
        "review", "Review", "0.3.0", "Community", ["Review.BasicApproval"],
        [new("changes", "Changes", "/changes", "Review", 10), new("queue", "Review queue", "/queue", "Review", 20)],
        [new("change", "overview", "Overview", "/api/review/changes/{id}", 10), new("change", "files", "Changes", "/api/review/changes/{id}", 20),
         new("change", "discussion", "Discussion", "/api/review/changes/{id}", 30), new("change", "reviewers", "Reviewers", "/api/review/changes/{id}", 40)]);
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ReviewSchemaInitializer>();
        services.AddSingleton<IChangeRepository, SqliteChangeRepository>();
        services.AddSingleton<ReviewService>();
        services.AddSingleton<CheckQueryService>();
    }
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapReviewEndpoints();
}
