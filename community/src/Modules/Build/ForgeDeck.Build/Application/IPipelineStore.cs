using ForgeDeck.Build.Domain;

namespace ForgeDeck.Build.Application;

public interface IPipelineStore
{
    IReadOnlyList<PipelineDefinition> ListDefinitions(Guid? projectId = null);
    PipelineDefinition? FindDefinition(Guid id);
    void SaveDefinition(Guid projectId, PipelineDefinition definition, bool recordVersion = true);
    void DeleteDefinition(Guid definitionId);
    string? GetDefinitionVersionPayload(Guid definitionId, int version);

    IReadOnlyList<PipelineRun> ListRuns(int take = 100);
    PipelineRun? FindRun(Guid id);
    IReadOnlyList<PipelineRun> FindRunsForChange(Guid changeId);
    IReadOnlyList<PipelineRun> FindActiveRunsForChange(Guid changeId, Guid definitionId);
    void SaveRun(PipelineRun run);
    IReadOnlyList<PipelineRun> ListRunsWithJobsWaitingForRunner();

    IReadOnlyList<RunnerAgent> ListRunners();
    RunnerAgent? FindRunner(Guid id);
    RunnerAgent? FindRunnerByTokenHash(string tokenHash);
    void SaveRunner(RunnerAgent runner);
    void RevokeRunner(Guid runnerId);

    void SaveRegistrationToken(RunnerRegistrationToken token);
    RunnerRegistrationToken? FindRegistrationTokenByHash(string tokenHash);
    void ConsumeRegistrationToken(Guid tokenId);
}
