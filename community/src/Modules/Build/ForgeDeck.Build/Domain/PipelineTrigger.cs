namespace ForgeDeck.Build.Domain;

public enum PipelineTrigger
{
    Manual,
    Push,
    ChangeOpened,
    ChangeUpdated,
    ChangeMerged
}
