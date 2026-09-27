using ForgeDeck.Deploy.Domain;

namespace ForgeDeck.Deploy.Application;

public interface IDeployStore
{
    IReadOnlyList<DeploymentEnvironment> ListEnvironments(Guid? projectId = null);
    DeploymentEnvironment? FindEnvironment(Guid id);
    void SaveEnvironment(DeploymentEnvironment environment);
    void DeleteEnvironment(Guid id);

    IReadOnlyList<Deployment> ListDeployments(Guid? projectId = null, Guid? environmentId = null, int take = 100);
    Deployment? FindDeployment(Guid id);
    void SaveDeployment(Deployment deployment);

    void SaveBuildRunReference(Guid runId, string pipelineName, string commitSha, Guid? changeId);
    void SaveArtifactReference(Guid runId, string pipelineName, string artifactName, string? uri);
    IReadOnlyList<DeployBuildRunReference> ListBuildRunReferences(int take = 50);
    IReadOnlyList<DeployArtifactReference> ListArtifactReferences(Guid? runId = null, int take = 50);
}
