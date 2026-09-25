using ForgeDeck.Build;
using ForgeDeck.Deploy;
using ForgeDeck.Git;
using ForgeDeck.Review;

namespace Core.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Optional_modules_do_not_reference_each_other()
    {
        var reviewReferences = typeof(ReviewModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        var pipelineReferences = typeof(BuildModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        var gitReferences = typeof(GitModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        var deployReferences = typeof(DeployModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("ForgeDeck.Build", reviewReferences);
        Assert.DoesNotContain("ForgeDeck.Review", pipelineReferences);
        Assert.DoesNotContain("ForgeDeck.Review", gitReferences);
        Assert.DoesNotContain("ForgeDeck.Build", gitReferences);
        Assert.DoesNotContain("ForgeDeck.Git", reviewReferences);
        Assert.DoesNotContain("ForgeDeck.Git", pipelineReferences);
        Assert.DoesNotContain("ForgeDeck.Deploy", reviewReferences);
        Assert.DoesNotContain("ForgeDeck.Deploy", pipelineReferences);
        Assert.DoesNotContain("ForgeDeck.Review", deployReferences);
        Assert.DoesNotContain("ForgeDeck.Build", deployReferences);
        Assert.DoesNotContain("ForgeDeck.Git", deployReferences);
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
        Assert.Equal("git", new ForgeDeck.Git.Application.GitDomainService().ServiceId);
        Assert.Equal("review", new ForgeDeck.Review.Application.ReviewDomainService().ServiceId);
        Assert.Equal("build", new ForgeDeck.Build.Application.BuildDomainService().ServiceId);
        Assert.Equal("deploy", new ForgeDeck.Deploy.Application.DeployDomainService().ServiceId);
        Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "docs", "architecture", "deployment-topology.md")));
        Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "compose.team-split.yaml")));
        Assert.Equal("scm", ForgeDeck.Contracts.Topology.DeploymentDomains.Scm);
        Assert.Equal("Build", ForgeDeck.Contracts.Topology.ModuleDatabases.ConnectionKeys.Build);
        Assert.Equal("Deploy", ForgeDeck.Contracts.Topology.ModuleDatabases.ConnectionKeys.Deploy);
        Assert.Equal(
            ForgeDeck.Contracts.Topology.DeploymentProfiles.TeamSplit,
            ForgeDeck.Contracts.Topology.DeploymentProfileResolver.Resolve("team-split"));
        Assert.Equal(
            ForgeDeck.Contracts.Topology.DeploymentProfiles.Appliance,
            ForgeDeck.Contracts.Topology.DeploymentProfileResolver.Resolve(null));
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
