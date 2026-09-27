using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Core.Extensions;
using ForgeDeck.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Core.Tests;

public sealed class BuildIdentityMigrationTests
{
    [Fact]
    public void EnsureCreated_rewrites_pipelines_runtime_id_to_build()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"forgedeck-mig-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PlatformDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using (var db = new PlatformDbContext(options))
            {
                PlatformDbContext.EnsureCreated(db);
                db.Extensions.Add(new ExtensionInstallation
                {
                    ExtensionId = "forgedeck.build",
                    Type = ExtensionType.Module,
                    State = ExtensionLifecycleState.Enabled,
                    Enabled = true,
                    RuntimeId = "pipelines",
                    InstalledVersion = "0.2.0",
                    InstalledAt = DateTimeOffset.UtcNow
                });
                db.SaveChanges();
            }

            using (var db = new PlatformDbContext(options))
            {
                PlatformDbContext.EnsureCreated(db);
                var row = db.Extensions.AsNoTracking().Single(e => e.ExtensionId == "forgedeck.build");
                Assert.Equal("build", row.RuntimeId);
            }
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Legacy_runtime_alias_normalizes_pipelines_to_build()
    {
        Assert.Equal("build", LegacyRuntimeAlias.Normalize("pipelines"));
        Assert.Equal("build", LegacyRuntimeAlias.Normalize("Pipelines"));
        Assert.Equal("review", LegacyRuntimeAlias.Normalize("review"));
        Assert.True(LegacyRuntimeAlias.EqualsCanonical("pipelines", "build"));
    }

    [Fact]
    public void Catalogue_build_runtime_id_is_canonical_build()
    {
        var entry = BuiltinExtensionCatalogue.Find("forgedeck.build");
        Assert.NotNull(entry);
        Assert.Equal("build", entry!.RuntimeId);
        Assert.Equal("forgedeck.build", BuiltinExtensionCatalogue.ExtensionIdForRuntime("pipelines"));
        Assert.Equal("forgedeck.build", BuiltinExtensionCatalogue.ExtensionIdForRuntime("build"));
    }
}
