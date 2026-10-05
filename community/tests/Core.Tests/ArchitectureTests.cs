using System.Text.RegularExpressions;
using ForgeDeck.Build;
using ForgeDeck.Build.Application;
using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Code;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Topology;
using ForgeDeck.Core.Extensions;
using ForgeDeck.Deploy;
using ForgeDeck.Deploy.Application;
using ForgeDeck.Deploy.Contracts.Events;
using ForgeDeck.Git;
using ForgeDeck.Git.Application;
using ForgeDeck.Git.Contracts.Events;
using ForgeDeck.Git.Infrastructure;
using ForgeDeck.Review;
using ForgeDeck.Review.Application;
using ForgeDeck.Review.Contracts.Events;

namespace Core.Tests;

/// <summary>
/// Frozen-boundary architecture invariants. These run in CI via Core.Tests.
/// Prefer failing here over accumulating platform abstractions.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly string[] OptionalModuleAssemblies =
    [
        "ForgeDeck.Review",
        "ForgeDeck.Build",
        "ForgeDeck.Deploy",
        "ForgeDeck.Git",
        "ForgeDeck.Code"
    ];

    [Fact]
    public void Core_does_not_reference_optional_module_implementations()
    {
        var coreProject = File.ReadAllText(Path.Combine(FindRepoRoot(), "community", "src", "Core", "ForgeDeck.Core", "ForgeDeck.Core.csproj"));
        foreach (var module in OptionalModuleAssemblies)
        {
            Assert.DoesNotContain(module, coreProject, StringComparison.Ordinal);
        }

        var coreReferences = typeof(ExtensionLifecycleService).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var module in OptionalModuleAssemblies)
        {
            Assert.DoesNotContain(module, coreReferences);
        }
    }

    [Fact]
    public void Optional_modules_do_not_reference_each_other()
    {
        var reviewReferences = typeof(ReviewModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        var buildReferences = typeof(BuildModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        var gitReferences = typeof(GitModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        var deployReferences = typeof(DeployModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        var codeReferences = typeof(CodeModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("ForgeDeck.Build", reviewReferences);
        Assert.DoesNotContain("ForgeDeck.Review", buildReferences);
        Assert.DoesNotContain("ForgeDeck.Review", gitReferences);
        Assert.DoesNotContain("ForgeDeck.Build", gitReferences);
        Assert.DoesNotContain("ForgeDeck.Git", reviewReferences);
        Assert.DoesNotContain("ForgeDeck.Git", buildReferences);
        Assert.DoesNotContain("ForgeDeck.Deploy", reviewReferences);
        Assert.DoesNotContain("ForgeDeck.Deploy", buildReferences);
        Assert.DoesNotContain("ForgeDeck.Review", deployReferences);
        Assert.DoesNotContain("ForgeDeck.Build", deployReferences);
        Assert.DoesNotContain("ForgeDeck.Git", deployReferences);
        Assert.DoesNotContain("ForgeDeck.Code", reviewReferences);
        Assert.DoesNotContain("ForgeDeck.Code", buildReferences);
        Assert.DoesNotContain("ForgeDeck.Code", gitReferences);
        Assert.DoesNotContain("ForgeDeck.Code", deployReferences);
        Assert.DoesNotContain("ForgeDeck.Git", codeReferences);
        Assert.DoesNotContain("ForgeDeck.Review", codeReferences);
        Assert.DoesNotContain("ForgeDeck.Build", codeReferences);
        Assert.DoesNotContain("ForgeDeck.Deploy", codeReferences);

        // Peer *.Contracts references are allowed for cross-module events.
        Assert.Contains("ForgeDeck.Git.Contracts", reviewReferences);
        Assert.Contains("ForgeDeck.Build.Contracts", reviewReferences);
        Assert.Contains("ForgeDeck.Git.Contracts", buildReferences);
        Assert.Contains("ForgeDeck.Review.Contracts", buildReferences);
    }

    [Fact]
    public void Module_api_routes_are_declared_on_manifests()
    {
        IPlatformModule[] modules =
        [
            new ReviewModule(),
            new BuildModule(),
            new DeployModule(),
            new GitModule(),
            new CodeModule()
        ];

        foreach (var module in modules)
        {
            Assert.NotNull(module.Manifest.ApiRoutePrefixes);
            Assert.NotEmpty(module.Manifest.ApiRoutePrefixes!);
            Assert.All(module.Manifest.ApiRoutePrefixes!, prefix =>
            {
                Assert.StartsWith("/api/", prefix, StringComparison.OrdinalIgnoreCase);
            });
        }

        Assert.Contains("/api/pipelines", new BuildModule().Manifest.ApiRoutePrefixes!);
        Assert.Contains("/api/deploy", new DeployModule().Manifest.ApiRoutePrefixes!);
        Assert.Contains("/api/code", new CodeModule().Manifest.ApiRoutePrefixes!);
    }

    [Fact]
    public void Canonical_build_runtime_identity_is_build_not_pipelines()
    {
        Assert.Equal("build", new BuildModule().Manifest.Id);
        Assert.Equal("build", new BuildDomainService().ServiceId);

        var buildEntry = BuiltinExtensionCatalogue.Find("forgedeck.build");
        Assert.NotNull(buildEntry);
        Assert.Equal("build", buildEntry!.RuntimeId);

        // Gate resolution must key Build as "build".
        var gates = ExtensionGateMiddleware.ResolveGates([new BuildModule()]);
        Assert.Contains(gates, g => g.RuntimeId == "build" && g.Prefix.Equals("/api/pipelines", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_source_assigns_pipelines_as_runtime_module_id()
    {
        var root = FindRepoRoot();
        var sources = Directory.GetFiles(Path.Combine(root, "community", "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "sdk"), "*.cs", SearchOption.AllDirectories));

        // Manifest / catalogue construction of runtime id "pipelines" is forbidden.
        // Nav route ids like NavigationItem("pipelines", ...) remain allowed.
        var forbidden = new Regex(
            """(?i)(?:RuntimeId\s*[:=]\s*"pipelines"|new\s+ModuleManifest\s*\(\s*"pipelines"|Manifest\s*\{[^}]*Id\s*=\s*"pipelines")""",
            RegexOptions.Compiled);

        foreach (var path in sources)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var text = File.ReadAllText(path);
            Assert.False(forbidden.IsMatch(text), $"Forbidden pipelines runtime id assignment in {relative}");
        }
    }

    [Fact]
    public void Module_events_use_stable_type_and_version()
    {
        IEnumerable<IEventContract> contracts =
        [
            ..GitEventContracts.All(),
            ..ReviewEventContracts.All(),
            ..BuildEventContracts.All(),
            ..DeployEventContracts.All()
        ];

        Assert.NotEmpty(contracts);
        foreach (var contract in contracts)
        {
            Assert.False(string.IsNullOrWhiteSpace(contract.Type));
            Assert.StartsWith("forgedeck.", contract.Type, StringComparison.OrdinalIgnoreCase);
            Assert.True(contract.Version > 0);
        }
    }

    [Fact]
    public void Code_uses_source_provider_abstractions_not_git_implementation()
    {
        var codeProject = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "community", "src", "Modules", "Code", "ForgeDeck.Code", "ForgeDeck.Code.csproj"));
        Assert.DoesNotContain("ForgeDeck.Git.csproj", codeProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ForgeDeck.Git\\", codeProject, StringComparison.OrdinalIgnoreCase);

        var codeReferences = typeof(CodeModule).Assembly.GetReferencedAssemblies().Select(r => r.Name).ToArray();
        Assert.DoesNotContain("ForgeDeck.Git", codeReferences);
        Assert.Equal("code", new CodeModule().Manifest.Id);
        Assert.Contains("source-browser", new CodeModule().Manifest.Provides ?? []);
    }

    [Fact]
    public void Deploy_execution_goes_through_IDeploymentExecutor()
    {
        var ctor = typeof(DeployService).GetConstructors().Single();
        Assert.Contains(ctor.GetParameters(), p => p.ParameterType == typeof(IDeploymentExecutor));
        Assert.True(typeof(ImmediateDeploymentExecutor).IsAssignableTo(typeof(IDeploymentExecutor)));
    }

    [Fact]
    public void Git_storage_goes_through_IGitObjectStore()
    {
        var ctor = typeof(GitRepositoryService).GetConstructors().Single();
        Assert.Contains(ctor.GetParameters(), p => p.ParameterType == typeof(IGitObjectStore));
        Assert.True(typeof(InMemoryGitObjectStore).IsAssignableTo(typeof(IGitObjectStore)));
        Assert.True(typeof(FileSystemGitObjectStore).IsAssignableTo(typeof(IGitObjectStore)));
    }

    [Fact]
    public void Community_review_does_not_reference_commercial_packages()
    {
        var reviewReferences = typeof(ReviewModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.DoesNotContain("ForgeDeck.Review.Team", reviewReferences);
        Assert.DoesNotContain("ForgeDeck.Review.Enterprise", reviewReferences);
        var buildReferences = typeof(BuildModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.DoesNotContain("ForgeDeck.Build.Team", buildReferences);
        Assert.DoesNotContain("ForgeDeck.Build.Enterprise", buildReferences);
        var deployReferences = typeof(DeployModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.DoesNotContain("ForgeDeck.Deploy.Team", deployReferences);
        Assert.DoesNotContain("ForgeDeck.Deploy.Enterprise", deployReferences);
    }

    [Fact]
    public void Community_projects_do_not_reference_commercial_tree()
    {
        var root = FindRepoRoot();
        var communityProjects = Directory.GetFiles(Path.Combine(root, "community", "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("ForgeDeck.Server.csproj", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(communityProjects);

        foreach (var project in communityProjects)
        {
            var text = File.ReadAllText(project);
            Assert.DoesNotContain("commercial\\", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("commercial/", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ForgeDeck.Review.Team", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ForgeDeck.Review.Enterprise", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ForgeDeck.Build.Team", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ForgeDeck.Build.Enterprise", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ForgeDeck.Deploy.Team", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ForgeDeck.Deploy.Enterprise", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Sdk_does_not_reference_community_server_or_modules()
    {
        var root = FindRepoRoot();
        var sdkProjects = Directory.GetFiles(Path.Combine(root, "sdk"), "*.csproj", SearchOption.AllDirectories);
        Assert.NotEmpty(sdkProjects);
        foreach (var project in sdkProjects)
        {
            var text = File.ReadAllText(project);
            Assert.DoesNotContain("ForgeDeck.Server", text, StringComparison.Ordinal);
            Assert.DoesNotContain("community\\src\\Modules", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("community/src/Modules", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("commercial\\", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("commercial/", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Repository_follows_community_commercial_licence_split()
    {
        var root = FindRepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "licenses", "AGPL-3.0.txt")));
        Assert.True(File.Exists(Path.Combine(root, "LICENSE.enterprise")));
        Assert.True(File.Exists(Path.Combine(root, "commercial", "LICENSE")));
        Assert.True(File.Exists(Path.Combine(root, "community", "LICENSE")));
        Assert.True(File.Exists(Path.Combine(root, "sdk", "LICENSE")));
        Assert.True(Directory.Exists(Path.Combine(root, "community")));
        Assert.True(Directory.Exists(Path.Combine(root, "commercial")));
        Assert.True(Directory.Exists(Path.Combine(root, "sdk")));

        var rootLicense = File.ReadAllText(Path.Combine(root, "LICENSE"));
        Assert.Contains("GNU AFFERO GENERAL PUBLIC LICENSE", rootLicense, StringComparison.OrdinalIgnoreCase);

        var commercialLicense = File.ReadAllText(Path.Combine(root, "commercial", "LICENSE"));
        Assert.Contains("Enterprise Edition", commercialLicense, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Domain_service_contracts_are_registered_by_first_party_modules()
    {
        Assert.Equal("git", new GitDomainService().ServiceId);
        Assert.Equal("review", new ReviewDomainService().ServiceId);
        Assert.Equal("build", new BuildDomainService().ServiceId);
        Assert.Equal("deploy", new DeployDomainService().ServiceId);
        Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "docs", "architecture", "deployment-topology.md")));
        Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "compose.team-split.yaml")));
        Assert.Equal("scm", DeploymentDomains.Scm);
        Assert.Equal("Build", ModuleDatabases.ConnectionKeys.Build);
        Assert.Equal("Deploy", ModuleDatabases.ConnectionKeys.Deploy);
        Assert.Equal(
            DeploymentProfiles.TeamSplit,
            DeploymentProfileResolver.Resolve("team-split"));
        Assert.Equal(
            DeploymentProfiles.Appliance,
            DeploymentProfileResolver.Resolve(null));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ForgeDeck.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
