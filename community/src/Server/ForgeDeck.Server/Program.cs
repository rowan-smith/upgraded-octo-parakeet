using System.Text.Json.Serialization;
using ForgeDeck.Connectors.GitHub;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Core;
using ForgeDeck.Core.Api;
using ForgeDeck.Core.Application;
using ForgeDeck.Core.Extensions;
using ForgeDeck.Core.Identity;
using ForgeDeck.Core.Modules;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Core.SourceControl;
using ForgeDeck.Messaging;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var safeMode = string.Equals(
    Environment.GetEnvironmentVariable("FORGEDECK_SAFE_MODE"),
    "true",
    StringComparison.OrdinalIgnoreCase)
    || builder.Configuration.GetValue("SafeMode", false);

IReadOnlyList<IPlatformModule> modules = safeMode
    ? Array.Empty<IPlatformModule>()
    : new ModuleDiscovery().Discover(builder.Configuration, AppContext.BaseDirectory);

builder.Services.AddPlatformCore(modules, builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 20;
    });
    options.AddFixedWindowLimiter("runner-register", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 10;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
if (!safeMode)
{
    builder.Services.AddGitHubConnector();
}

foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

var app = builder.Build();

var bootstrap = app.Configuration.GetSection("Bootstrap").Get<BootstrapOptions>() ?? new();
if (app.Environment.IsProduction() && bootstrap.IsDevelopmentDefault)
{
    throw new InvalidOperationException("Bootstrap:IsDevelopmentDefault must be false in Production.");
}

using (var platformDb = app.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContext())
{
    PlatformDbContext.EnsureCreated(platformDb);
}
app.Services.GetRequiredService<IMessagingBootstrapper>().EnsureCreated();

foreach (var registration in app.Services.GetServices<EventContractRegistration>())
{
    var registry = app.Services.GetRequiredService<IEventRegistry>();
    foreach (var contract in registration.Contracts)
    {
        if (registry.TryGet(contract.Type, contract.Version) is null)
        {
            registry.Register(contract);
        }
    }
}

if (!safeMode)
{
    app.Services.ActivateRegisteredHandlers();
}

app.Services.GetRequiredService<DevelopmentSeedService>().SeedIfEnabled();
app.Services.GetRequiredService<SourceConnectionSeeder>().Seed();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseMiddleware<SourceProviderExceptionMiddleware>();
app.UseExtensionGates();
app.UseMvpAuthentication();
app.MapAuthenticationEndpoints();
app.MapTenancyEndpoints();
app.MapPlatformEndpoints(modules);
app.MapExtensionEndpoints();
app.MapEventDiagnosticsEndpoints();
if (!safeMode)
{
    app.MapGitHubWebhookEndpoints();
}

app.MapSearchEndpoints();
app.MapAccessEndpoints();
app.MapIntegrationEndpoints();
app.MapSourceEndpoints();
app.MapLocalRepositoryEndpoints();
foreach (var module in modules)
{
    module.MapEndpoints(app);
}

app.Run();

public partial class Program;
