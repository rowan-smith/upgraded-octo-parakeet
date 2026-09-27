using System.Security.Cryptography;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Deploy.Contracts.Events;
using ForgeDeck.Deploy.Domain;
using ForgeDeck.Deploy.Infrastructure;

namespace ForgeDeck.Deploy.Application;

public sealed class DeployAgentService(
    IDeployStore store,
    IEventPublisher events)
{
    private static readonly TimeSpan HealthyWindow = TimeSpan.FromMinutes(2);

    public (Guid AgentId, string AgentToken) Register(string name, IReadOnlyList<string>? labels = null, string? registrationToken = null)
    {
        _ = registrationToken; // optional shared secret reserved for future lock-down

        var agentToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var agent = new DeploymentAgent
        {
            Name = string.IsNullOrWhiteSpace(name) ? "deploy-agent" : name.Trim(),
            TokenHash = EfDeployStore.HashToken(agentToken),
            Labels = labels ?? []
        };
        agent.Heartbeat(DeploymentAgentStatus.Online, agent.Labels);
        store.SaveAgent(agent);
        return (agent.Id, agentToken);
    }

    public DeploymentAgent Heartbeat(Guid agentId, string agentToken, IReadOnlyList<string>? labels = null)
    {
        var agent = Authenticate(agentId, agentToken);
        agent.Heartbeat(DeploymentAgentStatus.Online, labels ?? agent.Labels);
        store.SaveAgent(agent);
        return agent;
    }

    public IReadOnlyList<DeploymentAssignment> ClaimWork(Guid agentId, string agentToken, int maxJobs = 1)
    {
        var agent = Authenticate(agentId, agentToken);
        var take = Math.Max(1, maxJobs);
        var assignments = new List<DeploymentAssignment>();

        foreach (var deployment in store.ListPendingDeployments(take * 2))
        {
            if (assignments.Count >= take)
            {
                break;
            }

            if (deployment.Status != DeploymentStatus.Pending)
            {
                continue;
            }

            deployment.Status = DeploymentStatus.InProgress;
            deployment.AgentId = agent.Id;
            store.SaveDeployment(deployment);

            var releaseId = ResolveReleaseId(deployment);
            _ = events.PublishAsync(
                new DeployStartedEvent(deployment.Id, releaseId, deployment.EnvironmentId),
                DeployEventPublishOptions.Default);

            assignments.Add(new DeploymentAssignment(
                deployment.Id,
                deployment.EnvironmentId,
                deployment.ProjectId,
                deployment.Version,
                deployment.Notes,
                releaseId));
        }

        if (assignments.Count > 0)
        {
            agent.Heartbeat(DeploymentAgentStatus.Busy, agent.Labels);
            store.SaveAgent(agent);
        }

        return assignments;
    }

    public Deployment Complete(
        Guid agentId,
        string agentToken,
        Guid deploymentId,
        bool success,
        string? log = null)
    {
        var agent = Authenticate(agentId, agentToken);
        var deployment = store.FindDeployment(deploymentId)
            ?? throw new KeyNotFoundException("Deployment was not found.");

        if (deployment.AgentId is Guid assigned && assigned != agent.Id)
        {
            throw new UnauthorizedAccessException("Deployment is assigned to a different agent.");
        }

        if (deployment.Status is DeploymentStatus.Succeeded or DeploymentStatus.Failed or DeploymentStatus.RolledBack)
        {
            throw new InvalidOperationException($"Deployment is already {deployment.Status}.");
        }

        deployment.AgentId ??= agent.Id;
        deployment.AgentLog = string.IsNullOrWhiteSpace(log) ? deployment.AgentLog : log.Trim();
        var releaseId = ResolveReleaseId(deployment);

        if (success)
        {
            deployment.Status = DeploymentStatus.Succeeded;
            store.SaveDeployment(deployment);
            _ = events.PublishAsync(
                new DeploySucceededEvent(deployment.Id, releaseId, deployment.EnvironmentId),
                DeployEventPublishOptions.Default);
        }
        else
        {
            deployment.Status = DeploymentStatus.Failed;
            store.SaveDeployment(deployment);
            _ = events.PublishAsync(
                new DeployFailedEvent(deployment.Id, releaseId, deployment.EnvironmentId, log),
                DeployEventPublishOptions.Default);
        }

        agent.Heartbeat(DeploymentAgentStatus.Online, agent.Labels);
        store.SaveAgent(agent);
        return deployment;
    }

    public IReadOnlyList<object> ListAgents() =>
        store.ListAgents().Select(a => new
        {
            a.Id,
            a.Name,
            status = a.Status.ToString(),
            health = a.IsHealthy(HealthyWindow) ? "Healthy" : "Offline",
            a.Labels,
            a.LastHeartbeatAt,
            a.CreatedAt
        }).ToArray();

    public void Revoke(Guid agentId)
    {
        if (store.FindAgent(agentId) is null)
        {
            throw new KeyNotFoundException("Deploy agent was not found.");
        }

        store.RevokeAgent(agentId);
    }

    public DeploymentAgent Authenticate(Guid agentId, string agentToken)
    {
        var agent = store.FindAgent(agentId) ?? throw new UnauthorizedAccessException("Deploy agent was not found.");
        var hash = EfDeployStore.HashToken(agentToken);
        if (!string.Equals(agent.TokenHash, hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Deploy agent token is invalid.");
        }

        return agent;
    }

    private static Guid ResolveReleaseId(Deployment deployment) =>
        Guid.TryParse(deployment.Version, out var parsed) ? parsed : deployment.Id;
}

public sealed record DeploymentAssignment(
    Guid DeploymentId,
    Guid EnvironmentId,
    Guid ProjectId,
    string Version,
    string? Notes,
    Guid ReleaseId);
