using Modules.Review;
using Modules.Review.Application;
using Modules.Review.Domain;
using Platform.Contracts.Checks;
using Platform.Contracts.Events;
using Platform.Contracts.Licensing;
using Platform.Contracts.SourceControl;
using Platform.Core.Capabilities;
using Platform.Core.Context;
using Platform.Core.Licensing;
using Platform.Core.SourceControl;

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
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ExternalId = "1",
            ExternalNumber = 1,
            ExternalUrl = "https://github.com/org/repo/pull/1",
            Title = "Test",
            Description = "Test",
            Author = "A",
            SourceBranch = "feature",
            TargetBranch = "main",
            HeadCommit = "abc1234",
            BaseCommit = "def5678",
            Repository = new SourceRepository("github", "org", "repo", "main")
        };

        Assert.Empty(change.Approvals);
        Assert.False(change.CanMerge);
        change.Approve("Reviewer");

        Assert.Single(change.Approvals);
        Assert.Equal("Approved", change.Status);
        Assert.True(change.CanMerge);
    }

    [Fact]
    public void Changes_requested_blocks_merge_until_reapproval()
    {
        var change = CreateChange();
        change.Approve("Alex");
        Assert.True(change.CanMerge);
        change.RequestChanges("Alex", "Please rename the provider interface.");
        Assert.False(change.CanMerge);
        Assert.Equal("Changes Requested", change.Status);
    }

    [Fact]
    public void Discussion_resolution_and_outdated_comments_survive_synchronization()
    {
        var change = CreateChange();
        var comment = change.AddComment("Maya Chen", "Looks good", "src/A.cs", "right", 12, "abc1234");
        change.SetDiscussionResolution(comment.DiscussionId, "Maya Chen", true);
        Assert.True(change.Comments[0].Resolved);

        change.Synchronize(new ExternalChange("1", 1, change.ExternalUrl, "Updated", "desc", "A", "feature", "main",
            "zzzz999", "def5678", "Open", false, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null,
            new SourceDiff("def5678", "zzzz999", [new SourceDiffFile("src/A.cs", "@@", 1, 0)])));

        Assert.True(change.Comments[0].Outdated);
        Assert.True(change.Comments[0].Resolved);
        Assert.Contains(change.Activity, item => item.Type == "CommitsUpdated");
    }

    [Fact]
    public void Local_github_remote_detection_supports_https_and_ssh()
    {
        var https = Platform.Core.SourceControl.LocalRepositoryInspector.DetectGitHub("https://github.com/acme/platform.git");
        var ssh = Platform.Core.SourceControl.LocalRepositoryInspector.DetectGitHub("git@github.com:acme/platform.git");
        Assert.Equal("acme/platform", https?.FullName);
        Assert.Equal("acme/platform", ssh?.FullName);
    }

    [Fact]
    public void RequiredCheckEvaluator_rejects_passed_check_for_stale_commit()
    {
        var checks = new[]
        {
            new CheckResult("Pipelines", "Build", CheckStatus.Passed, "1.0s", CommitSha: "oldsha", CompletedAt: DateTimeOffset.UtcNow)
        };

        Assert.False(RequiredCheckEvaluator.IsSatisfied(checks, "newsha", "Build"));
        var evaluated = RequiredCheckEvaluator.EvaluateAll(checks, "newsha", ["Build"]);
        Assert.False(evaluated[0].Satisfied);
        Assert.Equal("Pending", evaluated[0].Status);
    }

    [Fact]
    public void RequiredCheckEvaluator_accepts_passed_check_for_current_commit()
    {
        var checks = new[]
        {
            new CheckResult("Pipelines", "Build", CheckStatus.Passed, "1.0s", CommitSha: "abc1234", CompletedAt: DateTimeOffset.UtcNow)
        };

        Assert.True(RequiredCheckEvaluator.IsSatisfied(checks, "abc1234", "Build"));
        var evaluated = RequiredCheckEvaluator.EvaluateAll(checks, "abc1234", ["Build"]);
        Assert.True(evaluated[0].Satisfied);
    }

    [Fact]
    public void Required_checks_block_merge_policy_when_unsatisfied()
    {
        var change = CreateChange();
        change.Approve("Alex");
        Assert.True(change.CanMerge);

        var blocked = new ApprovalPolicyResult(
            HasApproval: true,
            HasBlockingReview: false,
            ProviderMergeable: true,
            RequiredChecks: [new CheckRequirementStatus("Build", false, "Pending")]);

        Assert.False(blocked.ChecksSatisfied);
        Assert.False(blocked.Satisfied);

        var allowed = new ApprovalPolicyResult(
            HasApproval: true,
            HasBlockingReview: false,
            ProviderMergeable: true,
            RequiredChecks: [new CheckRequirementStatus("Build", true, "Passed")]);

        Assert.True(allowed.Satisfied);
    }

    [Fact]
    public void Empty_required_checks_preserve_phase1_satisfaction()
    {
        var result = new ApprovalPolicyResult(true, false, true, RequiredChecks: null);
        Assert.True(result.ChecksSatisfied);
        Assert.True(result.Satisfied);
    }

    [Fact]
    public async Task EvaluateMerge_skips_required_checks_when_no_providers()
    {
        var context = new PlatformContextStore();
        var capabilities = new CapabilityService(
            [new ReviewModule()],
            new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null));
        var policies = new ApprovalPolicyResolver(
            capabilities,
            context,
            new ReviewPolicyState { MinimumApprovals = 1 },
            []);
        var options = Microsoft.Extensions.Options.Options.Create(new ReviewOptions
        {
            RequiredChecks = ["Build", "Unit Tests", "Integration Tests", "E2E Tests"]
        });
        ICheckProvider[] providers = [];
        var service = new ReviewService(
            new InMemoryChangeRepository(),
            new SourceProviderRegistry([], []),
            new SourceRepositoryService(new InMemorySourceConnectionStore(), new SourceProviderRegistry([], []), context),
            context,
            new NoopEventPublisher(),
            options,
            policies,
            new CheckQueryService(providers),
            providers);

        var change = CreateChange();
        change.Approve("Alex");
        var result = await service.EvaluateMergeAsync(change);

        Assert.Null(result.RequiredChecks);
        Assert.True(result.ChecksSatisfied);
        Assert.True(result.Satisfied);
    }

    [Fact]
    public async Task EvaluateMerge_applies_required_checks_when_providers_exist()
    {
        var context = new PlatformContextStore();
        var capabilities = new CapabilityService(
            [new ReviewModule()],
            new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null));
        var policies = new ApprovalPolicyResolver(
            capabilities,
            context,
            new ReviewPolicyState { MinimumApprovals = 1 },
            []);
        var options = Microsoft.Extensions.Options.Options.Create(new ReviewOptions
        {
            RequiredChecks = ["Build"]
        });
        ICheckProvider[] providers = [new EmptyCheckProvider()];
        var service = new ReviewService(
            new InMemoryChangeRepository(),
            new SourceProviderRegistry([], []),
            new SourceRepositoryService(new InMemorySourceConnectionStore(), new SourceProviderRegistry([], []), context),
            context,
            new NoopEventPublisher(),
            options,
            policies,
            new CheckQueryService(providers),
            providers);

        var change = CreateChange();
        change.Approve("Alex");
        var result = await service.EvaluateMergeAsync(change);

        Assert.NotNull(result.RequiredChecks);
        Assert.Contains(result.RequiredChecks, c => c.Name == "Build" && !c.Satisfied);
        Assert.False(result.Satisfied);
    }

    private static Change CreateChange() => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        ExternalId = "1",
        ExternalNumber = 1,
        ExternalUrl = "https://github.com/org/repo/pull/1",
        Title = "Test",
        Description = "Test",
        Author = "A",
        SourceBranch = "feature",
        TargetBranch = "main",
        HeadCommit = "abc1234",
        BaseCommit = "def5678",
        Repository = new SourceRepository("github", "org", "repo", "main"),
        ProviderMergeable = true
    };

    private sealed class EmptyCheckProvider : ICheckProvider
    {
        public string Id => "empty";
        public Task<IReadOnlyList<CheckResult>> GetChecksAsync(Guid changeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CheckResult>>([]);
    }

    private sealed class NoopEventPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : IDomainEvent =>
            Task.CompletedTask;
    }

    private sealed class InMemoryChangeRepository : IChangeRepository
    {
        private readonly Dictionary<Guid, Change> _items = new();
        public IReadOnlyList<Change> List() => _items.Values.ToArray();
        public Change? Find(Guid id) => _items.GetValueOrDefault(id);
        public Change? Find(string owner, string name, string externalId) =>
            _items.Values.FirstOrDefault(c =>
                c.Repository.Owner == owner && c.Repository.Name == name && c.ExternalId == externalId);
        public void Add(Change change) => _items[change.Id] = change;
        public void Update(Change change) => _items[change.Id] = change;
    }

    private sealed class InMemorySourceConnectionStore : ISourceConnectionStore
    {
        public IReadOnlyList<SourceRepositoryConnection> List(Guid projectId) => [];
        public SourceRepositoryConnection? Find(Guid id) => null;
        public SourceRepositoryConnection? Find(Guid projectId, RepositoryId repositoryId) => null;
        public void Save(SourceRepositoryConnection connection) { }
    }
}
