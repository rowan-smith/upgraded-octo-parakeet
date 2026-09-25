namespace ForgeDeck.Contracts.Modules;

/// <summary>
/// Host responsible for starting/stopping module packages.
/// In-process is the current first-party path; out-of-process is the preferred third-party boundary.
/// </summary>
public interface IModuleHost
{
    ModuleRuntimeKind PreferredThirdPartyRuntime { get; }

    Task StartAsync(ModulePackageDocument package, IModuleContext context, CancellationToken cancellationToken = default);

    Task StopAsync(string moduleId, CancellationToken cancellationToken = default);

    bool IsRunning(string moduleId);
}
