namespace Modules.Pipelines.Domain;

public enum PipelineTrigger
{
    Manual,
    Push,
    ChangeOpened,
    ChangeUpdated,
    ChangeMerged
}
