using System.Text.Json.Serialization;
using ForgeDeck.Connectors.GitHub;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Core;
using ForgeDeck.Core.Api;
using ForgeDeck.Core.Application;
using ForgeDeck.Core.Extensions;
using ForgeDeck.Core.Modules;
using ForgeDeck.Core.SourceControl;

var builder = WebApplication.CreateBuilder(args);
var modules = new ModuleDiscovery().Discover(builder.Configuration, AppContext.BaseDirectory);

builder.Services.AddPlatformCore(modules, builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddGitHubConnector();
foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

var app = builder.Build();
app.Services.GetRequiredService<DevelopmentSeedService>().SeedIfEnabled();
app.Services.GetRequiredService<SourceConnectionSeeder>().Seed();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseMiddleware<SourceProviderExceptionMiddleware>();
app.UseExtensionGates();
app.UseMvpAuthentication();
app.MapAuthenticationEndpoints();
app.MapTenancyEndpoints();
app.MapPlatformEndpoints(modules);
app.MapExtensionEndpoints();
app.MapIntegrationEndpoints();
app.MapSourceEndpoints();
app.MapLocalRepositoryEndpoints();
foreach (var module in modules)
{
    module.MapEndpoints(app);
}

app.Run();

public partial class Program;
