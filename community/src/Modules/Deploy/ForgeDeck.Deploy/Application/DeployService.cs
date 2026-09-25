using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Context;
using ForgeDeck.Deploy.Domain;

namespace ForgeDeck.Deploy.Application;

public sealed class DeployService(
    IDeployStore store,
    ICapabilityService capabilities,
    PlatformContextStore context)
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
        string? notes = null)
    {
        _ = store.FindEnvironment(environmentId)
            ?? throw new KeyNotFoundException("Environment was not found.");
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Deployment version is required.");
        }

        var deployment = new Deployment
        {
            ProjectId = projectId,
            EnvironmentId = environmentId,
            Version = version.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            TriggeredBy = string.IsNullOrWhiteSpace(triggeredBy) ? "system" : triggeredBy.Trim(),
            Status = DeploymentStatus.Succeeded
        };
        store.SaveDeployment(deployment);
        return deployment;
    }

    public Deployment Rollback(Guid deploymentId, string triggeredBy)
    {
        var previous = store.FindDeployment(deploymentId)
            ?? throw new KeyNotFoundException("Deployment was not found.");
        if (previous.Status is DeploymentStatus.RolledBack)
        {
            throw new InvalidOperationException("Deployment was already rolled back.");
        }

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
        return rollback;
    }

    private void EnsureEnvironmentCapacity(Guid projectId)
    {
        if (capabilities.Has(context.Organisation.Id, KnownCapabilities.Deploy.MultiEnvironment))
        {
            return;
        }

        var count = store.ListEnvironments(projectId).Count;
        if (count >= CommunityLimits.DeployMaxEnvironments)
        {
            throw new LicenceRequiredException(KnownCapabilities.Deploy.MultiEnvironment);
        }
    }
}
