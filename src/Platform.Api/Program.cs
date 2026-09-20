using Connectors.GitHub;
using System.Text.Json.Serialization;
using Platform.Contracts.Modules;
using Platform.Core;
using Platform.Core.Api;
using Platform.Core.Modules;
using Platform.Core.SourceControl;

var builder = WebApplication.CreateBuilder(args);
var modules = new ModuleDiscovery().Discover(builder.Configuration, AppContext.BaseDirectory);

builder.Services.AddPlatformCore(modules, builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddGitHubConnector();
foreach (var module in modules) module.RegisterServices(builder.Services, builder.Configuration);

var app = builder.Build();
app.Services.GetRequiredService<SourceConnectionSeeder>().Seed();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseMiddleware<SourceProviderExceptionMiddleware>();
app.UseMvpAuthentication();
app.MapAuthenticationEndpoints();
app.MapPlatformEndpoints(modules);
app.MapIntegrationEndpoints();
app.MapSourceEndpoints();
foreach (var module in modules) module.MapEndpoints(app);

app.Run();

public partial class Program;
