using Connectors.GitHub;
using System.Text.Json.Serialization;
using Platform.Contracts.Modules;
using Platform.Core;
using Platform.Core.Api;
using Platform.Core.Application;
using Platform.Core.Extensions;
using Platform.Core.Modules;
using Platform.Core.SourceControl;

var builder = WebApplication.CreateBuilder(args);
var modules = new ModuleDiscovery().Discover(builder.Configuration, AppContext.BaseDirectory);

builder.Services.AddPlatformCore(modules, builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddGitHubConnector();
foreach (var module in modules) module.RegisterServices(builder.Services, builder.Configuration);

var app = builder.Build();
var isDevelopmentProfile = app.Environment.IsDevelopment()
    || app.Environment.EnvironmentName is "Full" or "Disabled";
app.Services.GetRequiredService<DevelopmentSeedService>().SeedIfEnabled(isDevelopmentProfile);
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
foreach (var module in modules) module.MapEndpoints(app);

app.Run();

public partial class Program;
