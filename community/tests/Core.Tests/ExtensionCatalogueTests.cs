using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Core.Extensions;

namespace Core.Tests;

public sealed class ExtensionCatalogueTests
{
    [Fact]
    public void Catalogue_separates_modules_and_connectors()
    {
        Assert.Contains(BuiltinExtensionCatalogue.All, e => e.ExtensionId == "forgedeck.review" && e.Type == ExtensionType.Module);
        Assert.Contains(BuiltinExtensionCatalogue.All, e => e.ExtensionId == "forgedeck.build" && e.RuntimeId == "pipelines");
        Assert.Contains(BuiltinExtensionCatalogue.All, e => e.ExtensionId == "forgedeck.github" && e.Type == ExtensionType.Connector);
        Assert.DoesNotContain(BuiltinExtensionCatalogue.All, e => e.ExtensionId == "forgedeck.github" && e.Type == ExtensionType.Module);
    }

    [Fact]
    public void Runtime_ids_map_to_stable_extension_ids()
    {
        Assert.Equal("forgedeck.review", BuiltinExtensionCatalogue.ExtensionIdForRuntime("review"));
        Assert.Equal("forgedeck.build", BuiltinExtensionCatalogue.ExtensionIdForRuntime("pipelines"));
        Assert.Equal("forgedeck.code", BuiltinExtensionCatalogue.ExtensionIdForRuntime("code"));
    }
}
