using Modules.Git;
using Modules.Pipelines;
using Modules.Review;
using Modules.Review.Commercial;

namespace Tests.Unit;

public sealed class ArchitectureTests
{
    [Fact]
    public void Optional_modules_do_not_reference_each_other()
    {
        var reviewReferences = typeof(ReviewModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        var pipelineReferences = typeof(PipelinesModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        var gitReferences = typeof(GitModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("Modules.Pipelines", reviewReferences);
        Assert.DoesNotContain("Modules.Review", pipelineReferences);
        Assert.DoesNotContain("Modules.Review", gitReferences);
        Assert.DoesNotContain("Modules.Pipelines", gitReferences);
        Assert.DoesNotContain("Modules.Git", reviewReferences);
        Assert.DoesNotContain("Modules.Git", pipelineReferences);
    }

    [Fact]
    public void Community_review_does_not_reference_ee_package()
    {
        var reviewReferences = typeof(ReviewModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.DoesNotContain("Modules.Review.Commercial", reviewReferences);
    }

    [Fact]
    public void Ee_review_depends_on_community_review()
    {
        var eeReferences = typeof(ReviewCommercialModule).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.Contains("Modules.Review", eeReferences);
    }

    [Fact]
    public void Repository_follows_gitlab_style_ce_ee_licence_split()
    {
        var root = FindRepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "licenses", "AGPL-3.0.txt")));
        Assert.True(File.Exists(Path.Combine(root, "ee", "LICENSE")));
        Assert.True(File.Exists(Path.Combine(root, "ee", "Modules.Review.Commercial", "LICENSE")));
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Modules.Review.Commercial")));
        Assert.False(File.Exists(Path.Combine(root, "licenses", "COMMERCIAL.txt")));

        var rootLicense = File.ReadAllText(Path.Combine(root, "LICENSE"));
        Assert.Contains("GNU AFFERO GENERAL PUBLIC LICENSE", rootLicense, StringComparison.OrdinalIgnoreCase);

        var eeLicense = File.ReadAllText(Path.Combine(root, "ee", "LICENSE"));
        Assert.Contains("Enterprise Edition", eeLicense, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Community Edition", eeLicense, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Affero", eeLicense, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ForgeDeck.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
