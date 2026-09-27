using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Context;
using ForgeDeck.Deploy.Contracts.Events;
using ForgeDeck.Deploy.Domain;

namespace ForgeDeck.Deploy.Application;

public sealed class DeployService(
    IDeployStore store,
    ICapabilityService capabilities,
    PlatformContextStore context,
    IEventPublisher events,
    IDeploymentExecutor executor)
{
    public IReadOnlyList<DeploymentEnvironment> ListEnvironments(Guid? projectId = null) =>
        store.ListEnvironments(projectId);

    public DeploymentEnvironment? FindEnvironment(Guid id) => store.FindEnvironment(id);

    public DeploymentEnvironment CreateEnvironment(Guid projectId, string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Environment name is required.");
        }

        EnsureEnvironmentCapacity(projectId);

        var environment = new DeploymentEnvironment
        {
            ProjectId = projectId,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        };
        store.SaveEnvironment(environment);
        return environment;
    }

    public void DeleteEnvironment(Guid id)
    {
        _ = store.FindEnvironment(id) ?? throw new KeyNotFoundException("Environment was not found.");
        store.DeleteEnvironment(id);
    }

    public IReadOnlyList<Deployment> ListDeployments(Guid? projectId = null, Guid? environmentId = null, int take = 100) =>
        store.ListDeployments(projectId, environmentId, take);

    public Deployment? FindDeployment(Guid id) => store.FindDeployment(id);

    public Deployment CreateDeployment(
        Guid projectId,
        Guid environmentId,
        string version,
        string triggeredBy,
        string? notes = null,
        Guid? releaseId = null)
    {
        _ = store.FindEnvironment(environmentId)
            ?? throw new KeyNotFoundException("Environment was not found.");
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Deployment version is required.");
        }

        var resolvedReleaseId = releaseId
            ?? (Guid.TryParse(version, out var parsed) ? parsed : Guid.NewGuid());

        var deployment = new Deployment
        {
            ProjectId = projectId,
            EnvironmentId = environmentId,
            Version = version.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            TriggeredBy = string.IsNullOrWhiteSpace(triggeredBy) ? "system" : triggeredBy.Trim(),
            Status = DeploymentStatus.Pending
        };
        store.SaveDeployment(deployment);
        _ = PublishAsync(new DeployQueuedEvent(deployment.Id, resolvedReleaseId, environmentId));

        executor.ExecuteAsync(deployment, resolvedReleaseId).GetAwaiter().GetResult();
        return store.FindDeployment(deployment.Id) ?? deployment;
    }

    public Deployment Rollback(Guid deploymentId, string triggeredBy)
    {
        var previous = store.FindDeployment(deploymentId)
            ?? throw new KeyNotFoundException("Deployment was not found.");
        if (previous.Status is DeploymentStatus.RolledBack)
        {
            throw new InvalidOperationException("Deployment was already rolled back.");
        }

        var releaseId = Guid.TryParse(previous.Version, out var parsed) ? parsed : previous.Id;

        previous.Status = DeploymentStatus.RolledBack;
        store.SaveDeployment(previous);

        var rollback = new Deployment
        {
            ProjectId = previous.ProjectId,
            EnvironmentId = previous.EnvironmentId,
            Version = previous.Version,
            Notes = $"Rollback of {previous.Id:N}",
            TriggeredBy = string.IsNullOrWhiteSpace(triggeredBy) ? "system" : triggeredBy.Trim(),
            Status = DeploymentStatus.Succeeded,
            RollbackOfId = previous.Id
        };
        store.SaveDeployment(rollback);
        _ = PublishAsync(new DeployRolledBackEvent(rollback.Id, previous.EnvironmentId, releaseId));
        return rollback;
    }

    public Deployment RollbackToRelease(Guid environmentId, Guid targetReleaseId, string triggeredBy, string projectKey)
    {
        _ = store.FindEnvironment(environmentId)
            ?? throw new KeyNotFoundException("Environment was not found.");

        var current = store.ListDeployments(environmentId: environmentId, take: 100)
            .FirstOrDefault(d => d.Status == DeploymentStatus.Succeeded);
        if (current is not null)
        {
            current.Status = DeploymentStatus.RolledBack;
            store.SaveDeployment(current);
        }

        var version = targetReleaseId.ToString("N");
        var projectId = current?.ProjectId
                        ?? store.FindEnvironment(environmentId)?.ProjectId
                        ?? context.Project.Id;

        var rollback = new Deployment
        {
            ProjectId = projectId,
            EnvironmentId = environmentId,
            Version = version,
            Notes = $"Rollback to release {targetReleaseId:N} ({projectKey})",
            TriggeredBy = string.IsNullOrWhiteSpace(triggeredBy) ? "system" : triggeredBy.Trim(),
            Status = DeploymentStatus.Succeeded,
            RollbackOfId = current?.Id
        };
        store.SaveDeployment(rollback);
        _ = PublishAsync(new DeployRolledBackEvent(rollback.Id, environmentId, targetReleaseId));
        return rollback;
    }

    public Deployment FailDeployment(Guid deploymentId, string? reason = null)
    {
        var deployment = store.FindDeployment(deploymentId)
            ?? throw new KeyNotFoundException("Deployment was not found.");
        deployment.Status = DeploymentStatus.Failed;
        store.SaveDeployment(deployment);
        var releaseId = Guid.TryParse(deployment.Version, out var parsed) ? parsed : deployment.Id;
        _ = PublishAsync(new DeployFailedEvent(deployment.Id, releaseId, deployment.EnvironmentId, reason));
        return deployment;
    }

    private Task PublishAsync<TEvent>(TEvent data) where TEvent : class =>
        events.PublishAsync(data, DeployEventPublishOptions.Default);

    private void EnsureEnvironmentCapacity(Guid projectId)
    {
        var limit = SoftLimits.MaxEnvironments(
            capabilities.Has(context.Organisation.Id, KnownCapabilities.Deploy.MultiEnvironment));
        if (limit is null)
        {
            return;
        }

        var count = store.ListEnvironments(projectId).Count;
        if (count >= limit.Value)
        {
            throw new LicenceRequiredException(KnownCapabilities.Deploy.MultiEnvironment);
        }
    }
}

internal static class DeployEventPublishOptions
{
    public static PublishOptions Default { get; } = new()
    {
        Actor = new EventActor(ActorType.Extension, "forgedeck.deploy", "Deploy"),
        Publisher = "forgedeck.deploy"
    };
}
