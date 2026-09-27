using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Core.Extensions;

public interface IExtensionRegistry
{
    IReadOnlyList<ExtensionInstallation> List();
    ExtensionInstallation? Find(string extensionId);
    bool IsEnabled(string extensionId);
    bool IsRuntimeEnabled(string runtimeId);
    void Save(ExtensionInstallation installation);
    void Delete(string extensionId);
    bool HasAnyInstalled();
}

public sealed class EfExtensionRegistry : IExtensionRegistry
{
    private readonly IDbContextFactory<PlatformDbContext> _factory;

    public EfExtensionRegistry(IDbContextFactory<PlatformDbContext> factory)
    {
        _factory = factory;
        using var db = _factory.CreateDbContext();
        PlatformDbContext.EnsureCreated(db);
    }

    public IReadOnlyList<ExtensionInstallation> List()
    {
        using var db = _factory.CreateDbContext();
        return db.Extensions.AsNoTracking().OrderBy(x => x.ExtensionId).ToList();
    }

    public ExtensionInstallation? Find(string extensionId)
    {
        using var db = _factory.CreateDbContext();
        return db.Extensions.AsNoTracking().FirstOrDefault(x => x.ExtensionId == extensionId);
    }

    public bool IsEnabled(string extensionId)
    {
        var row = Find(extensionId);
        return row is { Enabled: true, State: ExtensionLifecycleState.Enabled or ExtensionLifecycleState.RestartRequired };
    }

    public bool IsRuntimeEnabled(string runtimeId)
    {
        var entry = BuiltinExtensionCatalogue.FindByRuntimeId(runtimeId);
        return entry is not null && IsEnabled(entry.ExtensionId);
    }

    public bool HasAnyInstalled() => List().Any(x => x.State is not ExtensionLifecycleState.Available);

    public void Save(ExtensionInstallation installation)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.Extensions.Find(installation.ExtensionId);
        if (existing is null)
        {
            db.Extensions.Add(installation);
        }
        else
        {
            existing.Type = installation.Type;
            existing.RuntimeId = installation.RuntimeId;
            existing.InstalledVersion = installation.InstalledVersion;
            existing.State = installation.State;
            existing.Enabled = installation.Enabled;
            existing.InstalledAt = installation.InstalledAt;
            existing.InstalledBy = installation.InstalledBy;
            existing.UpdatedAt = installation.UpdatedAt;
            existing.LastError = installation.LastError;
            existing.RestartRequired = installation.RestartRequired;
        }

        db.SaveChanges();
    }

    public void Delete(string extensionId)
    {
        using var db = _factory.CreateDbContext();
        db.Extensions.Where(x => x.ExtensionId == extensionId).ExecuteDelete();
    }
}
