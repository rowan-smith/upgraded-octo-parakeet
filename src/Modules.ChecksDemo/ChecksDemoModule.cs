using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Contracts.Modules;

namespace Modules.ChecksDemo;

public sealed class ChecksDemoModule : IPlatformModule
{
    public ModuleManifest Manifest { get; } = new(
        "checks-demo", "Checks Demo", "0.1.0", "Community",
        ["Checks.Read"], [],
        [new("change", "checks", "Checks", "/api/checks/changes/{id}", 50)]);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/checks/changes/{id:guid}", (Guid id) => Results.Ok(new
        {
            changeId = id,
            checks = new[]
            {
                new { name = "Build", status = "Passed", duration = "1m 42s", provider = "Checks Demo" },
                new { name = "Unit tests", status = "Passed", duration = "2m 08s", provider = "Checks Demo" },
                new { name = "Integration tests", status = "Running", duration = "54s", provider = "Checks Demo" }
            }
        }));
}
