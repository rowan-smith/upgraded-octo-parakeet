using System.Reflection;
using ForgeDeck.Contracts.Modules;
using Microsoft.Extensions.Configuration;

namespace ForgeDeck.Core.Modules;

public sealed class ModuleDiscovery
{
    public IReadOnlyList<IPlatformModule> Discover(IConfiguration configuration, string baseDirectory)
    {
        LoadModuleAssemblies(baseDirectory);
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(IsPlatformModule)
            .Select(CreateModule)
            .Where(module => configuration.GetValue($"Modules:{module.Manifest.Id}:Enabled", true))
            .OrderBy(module => module.Manifest.Id)
            .ToArray();
    }

    private static readonly HashSet<string> ExcludedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "ForgeDeck.Server",
        "ForgeDeck.Core",
        "ForgeDeck.Contracts",
        "ForgeDeck.Web",
        "ForgeDeck.Runner"
    };

    private static void LoadModuleAssemblies(string baseDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(baseDirectory, "ForgeDeck.*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (ExcludedAssemblies.Contains(name))
            {
                continue;
            }

            try { Assembly.LoadFrom(file); } catch (BadImageFormatException) { }
        }
    }

    private static bool IsPlatformModule(Type type) =>
        type is { IsAbstract: false, IsInterface: false } && typeof(IPlatformModule).IsAssignableFrom(type);
    private static IPlatformModule CreateModule(Type type) => (IPlatformModule)Activator.CreateInstance(type)!;
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
    }
}
