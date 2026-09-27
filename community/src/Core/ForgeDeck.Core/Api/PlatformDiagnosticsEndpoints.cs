using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Application;
using ForgeDeck.Core.Extensions;
using ForgeDeck.Core.Identity;
using ForgeDeck.Core.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace ForgeDeck.Core.Api;

/// <summary>Aggregated operational diagnostics for events, extensions, agents, and instance upgrade state.</summary>
public static class PlatformDiagnosticsEndpoints
{
    public static RouteGroupBuilder MapPlatformDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/diagnostics");

        group.MapGet("/", async (
            IEventDiagnostics diagnostics,
            ExtensionLifecycleService extensions,
            SetupService setup,
            IConfiguration configuration,
            IServiceProvider services,
            PermissionAuthorizer authorizer,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage)
                && !authorizer.Has(http, OrganisationPermissions.AuditRead))
            {
                return PermissionAuthorizer.Forbidden();
            }

            var failures = await diagnostics.ListFailedDeliveriesAsync(25, cancellationToken);
            var backlog = await diagnostics.ListOutboxBacklogAsync(cancellationToken);
            var subscriptions = await diagnostics.ListSubscriptionsAsync(cancellationToken);
            var extensionHealth = extensions.List().Select(x => new
            {
                id = x.ExtensionId,
                name = x.Name,
                state = x.State.ToString(),
                health = x.Health.ToString(),
                version = x.Version,
                installedFrom = x.InstalledFrom.ToString(),
                packageDigest = x.PackageDigest,
                lastError = x.LastError,
                restartRequired = x.RestartRequired
            }).ToArray();

            // Optional Build/Deploy surfaces — resolve by type name to avoid Core→module references.
            var runners = TryInvokeList(services, "ForgeDeck.Build.Application.RunnerService", "ListRunners") ?? Array.Empty<object>();
            var deployAgents = TryInvokeList(services, "ForgeDeck.Deploy.Application.DeployAgentService", "ListAgents")
                               ?? Array.Empty<object>();

            var status = setup.GetStatus();
            var restartRequired = extensionHealth.Count(x => x.restartRequired);
            var failedExtensions = extensionHealth.Count(x =>
                string.Equals(x.health, "Failed", StringComparison.OrdinalIgnoreCase));

            return Results.Ok(new
            {
                generatedAt = DateTimeOffset.UtcNow,
                instance = new
                {
                    initialised = status.Initialised,
                    setupCompleted = status.SetupCompleted,
                    organisationName = status.OrganisationName,
                    hasLicence = status.HasLicence,
                    licenceMode = status.LicenceMode,
                    databaseProvider = DatabaseProvider.Resolve(configuration).ToString(),
                    softLimits = new
                    {
                        communityMaxConcurrentPipelines = SoftLimits.MaxConcurrentPipelines(hasBuildConcurrent: false),
                        communityMaxEnvironments = SoftLimits.MaxEnvironments(hasMultiEnvironment: false)
                    },
                    upgradeState = new
                    {
                        restartRequiredCount = restartRequired,
                        failedExtensionCount = failedExtensions,
                        ready = status.SetupCompleted && failedExtensions == 0
                    }
                },
                events = new
                {
                    failureCount = failures.Count,
                    backlogCount = backlog.Count,
                    subscriptionCount = subscriptions.Count,
                    failures,
                    backlog
                },
                extensions = extensionHealth,
                runners,
                deployAgents
            });
        });

        return group;
    }

    private static object? TryInvokeList(IServiceProvider services, string typeName, string methodName)
    {
        Type? type = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type is not null)
            {
                break;
            }
        }

        if (type is null)
        {
            return null;
        }

        var instance = services.GetService(type);
        if (instance is null)
        {
            return null;
        }

        var method = type.GetMethod(methodName, Type.EmptyTypes);
        var result = method?.Invoke(instance, null);
        return result;
    }
}
