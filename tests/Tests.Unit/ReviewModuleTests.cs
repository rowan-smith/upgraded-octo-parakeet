using Modules.Review;
using Modules.Review.Domain;
using Platform.Contracts.SourceControl;

namespace Tests.Unit;

public sealed class ReviewModuleTests
{
    [Fact]
    public void Manifest_exposes_navigation_capability_and_extension_tabs()
    {
        var manifest = new ReviewModule().Manifest;

        Assert.Contains(manifest.Navigation, item => item.Route == "/changes");
        Assert.Contains("Review.BasicApproval", manifest.Capabilities);
        Assert.Equal(["overview", "files", "discussion", "reviewers"], manifest.ResourceTabs.Select(tab => tab.Id));
    }

    [Fact]
    public void Merge_policy_requires_an_approval()
    {
        var change = new Change
        {
            Id = Guid.NewGuid(), ExternalId = "1", Title = "Test", Description = "Test", Author = "A",
            SourceBranch = "feature", TargetBranch = "main", CommitSha = "abc1234",
            Repository = new SourceRepository("github", "org", "repo", "main")
        };

        Assert.Empty(change.Approvals);
        Assert.False(change.CanMerge);
        change.Approve("Reviewer");

        Assert.Single(change.Approvals);
        Assert.Equal("Approved", change.Status);
        Assert.True(change.CanMerge);
    }
}
