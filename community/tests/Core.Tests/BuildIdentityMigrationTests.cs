using ForgeDeck.Core.Extensions;

namespace Core.Tests;

public sealed class BuildIdentityMigrationTests
{
    [Fact]
    public void Catalogue_build_runtime_id_is_canonical_build()
    {
        var entry = BuiltinExtensionCatalogue.Find("forgedeck.build");
        Assert.NotNull(entry);
        Assert.Equal("build", entry!.RuntimeId);
        Assert.Equal("forgedeck.build", BuiltinExtensionCatalogue.ExtensionIdForRuntime("build"));
    }
}
