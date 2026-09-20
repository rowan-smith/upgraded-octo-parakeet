using Modules.Git;
using Modules.Pipelines;
using Modules.Review;

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
}
