# Phase 1 — Working Review for Local Repository

## 1. Objective

Implement a complete, usable code-review workflow for a repository being actively developed on the same machine as the platform.

The platform runs locally.

The developer continues using their normal local Git workflow:

```text
Local Working Repository
        |
        | git commit / git push
        v
      GitHub
        |
        | source provider
        v
 Local Platform
        |
        v
      Review
```

The platform becomes the primary interface for:

```text
Repository visibility
Changes / Pull Requests
Diffs
Comments
Reviewers
Approvals
Merge
Review history
```

GitHub remains responsible for:

```text
Git repository storage
Branches
Commits
Refs
Push / fetch
Pull Request persistence
Merge execution
```

The platform must not require native Git hosting.

---

# 2. Phase 1 success condition

Phase 1 is complete when the platform can be used to review development of its own repository.

The following workflow must work end-to-end:

```text
1. Run the platform locally.

2. Register the platform's local repository.

3. Detect/configure its GitHub remote.

4. Create a feature branch using normal Git.

5. Make changes locally.

6. Commit changes.

7. Push the feature branch to GitHub.

8. Open the platform.

9. Create a Change from:
       feature/foo -> main

10. View:
       commits
       changed files
       diff

11. Add:
       general comments
       inline comments

12. Request a reviewer.

13. Approve or request changes.

14. Evaluate merge requirements.

15. Merge from the platform.

16. Confirm GitHub PR is merged.

17. Pull main locally.

18. Continue development.
```

The next feature of the platform should realistically be developable using this workflow.

---

# 3. Scope

Implement:

```text
Core
Review Module
GitHub Connector
Local Repository Association
Repository Browser
Changes
Diffs
Comments
Reviews
Approvals
Merge
Audit
```

Do not implement:

```text
Native Git hosting
Git SSH server
Git HTTP server

Pipelines
Runners
CI

Deployment

Artifact registry

Ticketing

Advanced enterprise review policy

GitHub Actions integration

Repository mirroring

Automatic local working-tree modification
```

---

# 4. Architectural goal

Review must not depend directly on GitHub.

Review consumes provider-neutral contracts.

```text
                     ISourceProvider
                           |
          +----------------+----------------+
          |                |                |
          v                v                v

 GitHub Connector     Future GitLab     Future Native Git
                          Connector          Module
          |
          +----------------+
                           |
                           v
                        Review
```

GitHub is the first source-provider implementation.

Later:

```text
GitHub
GitLab
Azure Repos
Native Git

    ↓

ISourceProvider

    ↓

Review
```

must all be possible without changing Review business logic.

---

# 5. Source of truth

For Phase 1:

## GitHub owns

```text
Repository refs
Branches
Commits
Tags

Pull Request external state

Mergeability

Merge execution

Merged commit
```

## Platform owns

```text
Review UX

Platform reviewer assignments

Platform discussions

Platform approval state

Merge-policy evaluation

Review activity

Audit history
```

Some platform review data may later be synchronised back to GitHub, but this is not required for Phase 1.

---

# 6. Local repository association

A Project may be associated with a local repository path.

Example:

```text
Project
    Platform

Local Repository
    C:\Development\Platform

Remote
    origin

Remote URL
    https://github.com/example/platform.git

Default Branch
    main
```

The local path exists for developer convenience.

It must not become the authoritative Review data source.

---

# 7. Local repository detection

When configuring a Project, allow:

```text
[ Connect Local Repository ]
```

The user chooses or enters:

```text
C:\Development\Platform
```

The platform should detect:

```text
.git repository
repository root
current branch
HEAD commit
configured remotes
origin URL
default candidate remote
```

If `origin` points to GitHub:

```text
https://github.com/acme/platform.git
```

or:

```text
git@github.com:acme/platform.git
```

the platform should offer:

```text
Detected GitHub Repository

acme/platform

[ Connect GitHub ]
```

---

# 8. Local repository usage

The local repository may be used for:

```text
displaying local path
displaying current branch
displaying current HEAD
displaying clean / dirty working-tree status
detecting Git remote
opening repository folder
future editor integration
```

The Review system must operate against committed remote refs.

Uncommitted local changes are not part of a Change.

---

# 9. Working-tree status

Project overview may display:

```text
Local Repository

Path
C:\Development\Platform

Branch
feature/review-comments

HEAD
a71c991

Working Tree
3 modified files

Remote
origin → GitHub

Remote Status
2 commits ahead
```

This is informational only.

The platform must not automatically:

```text
git add
git commit
git checkout
git reset
git rebase
git push
```

the developer's active working copy.

---

# 10. GitHub connector

Implement a real GitHub connector.

It should provide:

```text
repository metadata
branches
commits
file trees
file contents
branch comparisons
pull requests
pull request creation
pull request state
mergeability
merge
close
```

The connector must translate GitHub-specific data into platform contracts.

GitHub DTOs must not escape the connector layer.

---

# 11. GitHub authentication

Support authenticated access using:

```text
GitHub Personal Access Token
```

for the initial implementation.

The token must:

```text
be encrypted at rest
never be returned through normal APIs
never appear in logs
be replaceable
be removable
```

The connector design must permit future GitHub App authentication.

---

# 12. Source-provider contract

Define a provider-neutral source abstraction.

Conceptually:

```csharp
public interface ISourceProvider
{
    Task<SourceRepository> GetRepositoryAsync(
        RepositoryId repositoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceBranch>> GetBranchesAsync(
        RepositoryId repositoryId,
        CancellationToken cancellationToken);

    Task<SourceCommit> GetCommitAsync(
        RepositoryId repositoryId,
        string commitSha,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceCommit>> GetCommitsAsync(
        RepositoryId repositoryId,
        string reference,
        CancellationToken cancellationToken);

    Task<SourceTree> GetTreeAsync(
        RepositoryId repositoryId,
        string reference,
        string? path,
        CancellationToken cancellationToken);

    Task<SourceFile> GetFileAsync(
        RepositoryId repositoryId,
        string reference,
        string path,
        CancellationToken cancellationToken);

    Task<SourceDiff> GetDiffAsync(
        RepositoryId repositoryId,
        string baseReference,
        string headReference,
        CancellationToken cancellationToken);
}
```

Exact signatures may change.

The abstraction boundary must not.

---

# 13. Change-provider contract

Review also requires an abstraction for remote change/PR operations.

Conceptually:

```csharp
public interface IChangeProvider
{
    Task<IReadOnlyList<ExternalChange>> GetChangesAsync(...);

    Task<ExternalChange> GetChangeAsync(...);

    Task<ExternalChange> CreateChangeAsync(...);

    Task<SourceDiff> GetChangeDiffAsync(...);

    Task<MergeabilityResult> GetMergeabilityAsync(...);

    Task<MergeResult> MergeAsync(...);

    Task CloseAsync(...);
}
```

GitHub maps this to Pull Requests.

A future native Git implementation may provide its own implementation.

---

# 14. Platform project layout

Project navigation for Phase 1:

```text
Project: Platform

Overview

Code
├── Files
├── Commits
└── Branches

Review
├── Changes
└── Review Queue

Settings
```

`Code` means repository visibility.

It does not mean the platform hosts the repository.

---

# 15. Repository browser

Provide read-only repository browsing.

Support:

```text
browse directories
browse files
switch branch/reference
view text file
view basic metadata
navigate breadcrumbs
```

Example:

```text
platform / main

src/
├── Platform.Api/
├── Platform.Core/
├── Platform.Contracts/
├── Modules/
│   └── Review/
└── Connectors/
    └── GitHub/
```

---

# 16. File viewer

Text files should support:

```text
syntax-aware display where practical
line numbers
copy path
branch/reference indicator
```

Example:

```text
src/Modules/Review/Change.cs

main
a71c991

  1  namespace Platform.Review;
  2
  3  public sealed class Change
  4  {
...
```

Binary-file rendering is not required.

---

# 17. Branches

Branches page displays:

```text
branch name
latest commit
short SHA
commit message
author
last updated
default-branch indicator
```

Example:

```text
main                       a81130d
feature/review-comments    bd8931a
feature/github-provider    17ac220
```

If a branch has no active Change:

```text
[ Create Change ]
```

---

# 18. Commits

Commits page displays:

```text
SHA
message
author
timestamp
```

Selecting a commit displays:

```text
metadata
parent
changed files
diff
```

---

# 19. Change domain

Review's primary aggregate is:

```text
Change
```

not:

```text
PullRequest
```

because Pull Request is provider-specific terminology.

A Change should contain:

```text
Id
ProjectId
RepositoryId

Provider
ExternalId
ExternalNumber
ExternalUrl

Title
Description

Author

SourceBranch
TargetBranch

BaseCommit
HeadCommit

Status

CreatedAt
UpdatedAt
MergedAt
ClosedAt
```

---

# 20. Change states

Initial states:

```text
Draft
Open
ChangesRequested
Approved
Mergeable
Merged
Closed
```

Do not assume every state must be persisted independently.

Some may be derived from:

```text
provider state
review state
policy result
```

---

# 21. Creating a Change

From a branch:

```text
feature/review-comments

[ Create Change ]
```

Form:

```text
Create Change

Source
feature/review-comments

Target
main

Title
Add inline review comments

Description
...

[ Create ]
```

The platform:

```text
creates corresponding GitHub PR
stores its external identity
creates platform Change state
redirects to Change page
```

---

# 22. Importing an existing Pull Request

Existing GitHub Pull Requests must be importable.

```text
GitHub PR #28
       ↓
Platform Change #28
```

The platform should also discover existing PRs for the connected repository.

Do not create duplicate Changes for the same external PR.

---

# 23. Change list

Display:

```text
Open
Draft
Approved
Changes Requested
Merged
Closed
```

Each entry includes:

```text
number
title
author
source branch
target branch
review state
updated time
```

Initial filtering:

```text
status
author
reviewer
```

---

# 24. Change page

The Change page is the central Phase 1 experience.

Example:

```text
Change #31

Implement review discussions

feature/review-discussions → main

Open

+418 -72
12 files changed
3 commits

------------------------------------------------

Overview
Changes
Discussion
Reviewers
```

---

# 25. Change header

Display:

```text
title
description summary
author
status
source branch
target branch

commits
files changed
additions
deletions

provider
external PR number

mergeability
review state
```

---

# 26. Overview tab

Display:

```text
description

commit summary

review status

requested reviewers

merge requirements

recent activity
```

Example:

```text
Review

Rowan                    Approved
Alex                     Waiting

Merge Requirements

✓ Required approval
✓ No blocking review
✓ Branch mergeable

Ready to merge
```

---

# 27. Changes tab

Provide a usable code-review diff.

Support:

```text
file list

added files
modified files
deleted files
renamed files where available

unified diff

line numbers

diff hunks

additions/deletions

collapse/expand file
```

Do not implement semantic diffing.

Use the source provider's comparison/diff capabilities.

---

# 28. Diff performance

Large Changes must not require loading the entire diff into the initial response.

Design for:

```text
file-level lazy loading
per-file diff loading
reasonable response-size limits
```

Exact optimisation can remain simple initially.

Do not architect the UI such that all future large Changes require a rewrite.

---

# 29. General comments

The Discussion tab supports general Change discussion.

Example:

```text
Rowan
Should this provider abstraction move into Platform.Contracts?

    Alex
    Yes, that makes sense.

    Rowan
    Updated in bd8931a.
```

Support:

```text
create comment
reply
edit own comment
delete own comment where allowed
```

---

# 30. Inline comments

A developer may select a changed line and comment.

Example:

```text
41 + public async Task Handle(...)
42 + {
43 +     await github.Get(...);
            │
            └── Comment

This probably belongs behind ISourceProvider.
```

Inline comments should store:

```text
ChangeId
FilePath
CommitSha
DiffSide
Line
Body
Author
CreatedAt
```

Retain enough positioning information to redisplay the discussion after refresh.

---

# 31. Discussion threads

Inline comments form discussions.

```text
Discussion
├── Root Comment
├── Reply
├── Reply
└── Status
```

Status:

```text
Open
Resolved
```

Support:

```text
reply
resolve
reopen
```

---

# 32. New commits

A Change may receive new commits after review begins.

When the source branch changes:

```text
old HEAD
   ↓
new HEAD
```

the platform should:

```text
detect the new HEAD
refresh commit information
refresh diff information
retain review history
retain discussions
```

Old inline discussions must not simply disappear.

If their original diff location cannot be mapped cleanly, show them as:

```text
Outdated
```

---

# 33. Reviewers

Support:

```text
request reviewer
remove reviewer
view review status
```

Reviewer states:

```text
Requested
Approved
ChangesRequested
```

For local single-developer dogfooding, support a development option:

```text
AllowSelfReview = true
```

Do not make self-review a fundamental production assumption.

---

# 34. Submit Review

Provide:

```text
[ Review Changes ]
```

Dialog:

```text
Review summary

...

○ Comment
○ Approve
○ Request Changes

[ Submit Review ]
```

A review may include:

```text
summary
review decision
pending inline discussions
```

---

# 35. Basic approval policy

Phase 1 requires only a basic policy.

Merge requirements:

```text
At least one approval

No active Changes Requested review

Source provider reports Change mergeable
```

Example:

```text
Merge Requirements

✓ 1 approval
✓ No changes requested
✓ GitHub reports branch mergeable

Ready to merge
```

Advanced policy is out of scope.

---

# 36. Future approval capabilities

Architecture should permit later capabilities such as:

```text
Review.MultiApproval
Review.TeamApproval
Review.CodeOwners
Review.PathApproval
Review.ConditionalApproval
Review.AllDiscussionsResolved
Review.RequiredChecks
```

Do not implement them in Phase 1.

Do not design Phase 1 in a way that prevents them.

---

# 37. Merge

When requirements are satisfied:

```text
[ Merge ]
```

Phase 1 needs at least:

```text
Merge commit
```

Squash and rebase may come later.

Flow:

```text
User
   ↓
Review Application
   ↓
Merge Policy Evaluation
   ↓
IChangeProvider
   ↓
GitHub Connector
   ↓
GitHub
```

Review must never call GitHub directly.

---

# 38. Merge confirmation

Display a confirmation before execution.

Example:

```text
Merge Change #31?

feature/review-discussions
        ↓
main

All merge requirements have passed.

[ Cancel ] [ Merge ]
```

---

# 39. Merge result

After successful merge:

```text
Change #31

Merged

feature/review-discussions → main

Merged commit
ab31f20

Merged by
Rowan

[ View Commit ]
```

GitHub must also report the PR as merged.

---

# 40. Merge conflict

If GitHub reports the Change is not mergeable:

```text
Cannot Merge

The source and target branches contain conflicts.

Resolve the conflicts locally and push the updated branch.
```

Do not attempt to build a browser conflict editor in Phase 1.

---

# 41. Close Change

Support:

```text
[ Close Change ]
```

without merging.

Closing should:

```text
close external GitHub PR
retain platform review history
retain comments
retain audit trail
```

---

# 42. Synchronisation

External GitHub state may change without going through the platform.

Examples:

```text
new commit pushed
PR title changed
PR merged through GitHub
PR closed through GitHub
branch deleted
```

Phase 1 should support:

```text
refresh on Change load
manual Refresh action
periodic background refresh
```

Webhooks are optional for Phase 1.

---

# 43. State ownership during synchronisation

Provider-owned fields should update from GitHub:

```text
branches
commits
head SHA
base SHA
title
description
external PR state
mergeability
```

Platform-owned state must remain:

```text
platform comments
platform reviewer state
platform review decisions
platform activity
platform audit
```

Do not destroy review history because provider state changed.

---

# 44. Activity timeline

Changes should have a human-readable timeline.

Example:

```text
10:21 Rowan created this Change

10:24 Alex was requested for review

10:48 Alex commented on PlatformModule.cs

11:02 Alex requested changes

11:31 Rowan pushed 2 commits

11:42 Alex approved

11:47 Rowan merged into main
```

Events include:

```text
Change created
Reviewer requested
Comment added
Discussion resolved
Review submitted
Changes requested
Approval
New commits
Change merged
Change closed
```

---

# 45. Audit

Core audit events should include:

```text
review.change.created
review.change.imported

review.comment.created
review.comment.edited
review.comment.deleted

review.discussion.resolved
review.discussion.reopened

review.reviewer.requested
review.reviewer.removed

review.approved
review.changes_requested

review.change.merged
review.change.closed
```

Audit and activity timeline are separate concepts.

---

# 46. Review queue

Provide:

```text
Review Queue
```

with:

```text
Waiting for me
Authored by me
Recently reviewed
```

Example:

```text
Waiting for me

#31 Implement discussions
#36 GitHub connector cleanup

Authored by me

#38 Repository browser
```

---

# 47. Project overview

Phase 1 Project overview may show:

```text
Open Changes           4

Waiting for Review     2

Approved               1

Recently Merged        5
```

Do not show concepts belonging to future modules.

---

# 48. Module behaviour

Review must remain an optional module.

When Review is disabled:

```text
Review navigation disappears

/api/review/* disappears

Review routes disappear

Review handlers are not registered

Review permissions are not registered
```

Core and source-provider configuration continue functioning independently.

---

# 49. Review permissions

Register:

```text
review.read

review.comment

review.request

review.approve

review.merge

review.manage
```

All enforcement must occur server-side.

Frontend visibility is not authorisation.

---

# 50. Repository permissions

For Phase 1, repository visibility may follow Project access.

Design so future permissions can support:

```text
source.read
source.write
source.admin
```

Do not over-engineer repository ACLs yet.

---

# 51. Frontend extension points

The Change page must support future module extensions.

Reserve an extension model such as:

```text
Change
├── Overview
├── Changes
├── Discussion
├── Reviewers
│
├── [Future Checks]
├── [Future Artifacts]
└── [Future Deployments]
```

Phase 2 will use this to add pipeline results.

Review must not need architectural redesign to display Checks later.

---

# 52. Phase 2 compatibility requirement

Phase 1 MUST leave an explicit concept for external checks.

A Change should be capable of later displaying:

```text
Checks

Unit Tests          Passed
Integration Tests   Passed
E2E                  Running
```

Do not implement pipeline checks yet.

Do provide the extension point/contracts needed to attach them later.

---

# 53. Backend structure

Target structure:

```text
src/

Platform.Core/
Platform.Contracts/
Platform.Api/
Platform.Web/

Modules/

  Review/
    Review.Domain/
    Review.Application/
    Review.Infrastructure/

Connectors/

  GitHub/
    GitHubConnector/

Tests/

  Unit/
  Integration/
```

Do not place GitHub implementation details in Review.

---

# 54. Persistence

Use durable relational persistence.

Core owns:

```text
core.organisations

core.projects

core.users

core.teams

core.integrations

core.audit
```

Review owns:

```text
review.changes

review.reviewers

review.reviews

review.discussions

review.comments

review.activity
```

A module must not directly query another module's tables.

---

# 55. External identity

Persist external references explicitly.

Example:

```text
Provider
github

Repository
acme/platform

ExternalChangeId
PR internal ID

ExternalChangeNumber
31

ExternalUrl
https://github.com/acme/platform/pull/31
```

Do not use GitHub IDs as primary domain IDs.

---

# 56. API surface

Indicative APIs:

```text
/api/projects/{projectId}/source

/api/projects/{projectId}/source/repository

/api/projects/{projectId}/source/branches

/api/projects/{projectId}/source/commits

/api/projects/{projectId}/source/tree

/api/projects/{projectId}/review/changes

/api/projects/{projectId}/review/changes/{changeId}

/api/projects/{projectId}/review/changes/{changeId}/diff

/api/projects/{projectId}/review/changes/{changeId}/comments

/api/projects/{projectId}/review/changes/{changeId}/discussions

/api/projects/{projectId}/review/changes/{changeId}/reviewers

/api/projects/{projectId}/review/changes/{changeId}/reviews

/api/projects/{projectId}/review/changes/{changeId}/merge

/api/projects/{projectId}/review/changes/{changeId}/close
```

Exact resource naming may differ.

Preserve domain boundaries.

---

# 57. Error handling

Handle:

```text
GitHub unavailable

invalid token

expired/revoked token

repository inaccessible

branch deleted

remote branch not pushed

PR closed externally

PR merged externally

merge conflict

force push

rate limiting

provider timeout
```

The UI must distinguish:

```text
Provider Error

Authentication Error

Permission Error

Merge Conflict

Policy Failure

Validation Error
```

Do not surface all failures as generic HTTP 500 errors.

---

# 58. Testing

## Unit tests

Cover:

```text
Change state

review decisions

basic approval policy

merge eligibility

discussion resolution

permission enforcement

provider/domain mappings
```

## Integration tests

Cover:

```text
Review enabled

Review disabled

GitHub provider registered

repository configured

Change imported

Change created

comment created

discussion resolved

review approved

changes requested

merge allowed

merge blocked

provider errors
```

Use deterministic fake GitHub responses for automated tests.

Do not make the normal test suite depend on live GitHub.

---

# 59. Real GitHub smoke test

Provide a manually invoked smoke/integration test path against a real test repository.

This should verify:

```text
authenticate

load repository

list branches

create PR

read diff

merge PR
```

Do not run this by default in the normal test suite.

---

# 60. Dogfood acceptance test

This is the primary Phase 1 acceptance test.

## Starting state

```text
The platform is running locally.

Its own repository is connected.

GitHub is configured.

main is up to date locally.
```

## Development

```bash
git checkout -b dogfood/phase-one-review

# modify the platform

git add .

git commit -m "Dogfood Phase 1 review"

git push -u origin dogfood/phase-one-review
```

## Review

Open the local platform.

Navigate:

```text
Project
    Platform
        Review
```

Create:

```text
dogfood/phase-one-review
        ↓
main
```

Verify:

```text
correct commits

correct files

correct diff

correct branch refs
```

Add:

```text
general comment

inline comment

reviewer
```

Submit:

```text
Approve
```

Verify:

```text
approval requirement passes

Change becomes mergeable
```

Merge through the platform.

Verify:

```text
platform Change = Merged

GitHub PR = Merged

merged commit exists on main
```

Then locally:

```bash
git checkout main
git pull
```

Verify the merged implementation is present.

---

# 61. Phase 1 completion criteria

Phase 1 is complete only when:

```text
The platform can connect to its own repository.

The local repository is recognised.

GitHub acts as a provider rather than leaking into Review.

Repository files can be browsed.

Branches can be browsed.

Commits can be browsed.

A Change can be created.

An existing PR can be imported.

A correct diff can be reviewed.

General comments work.

Inline discussions work.

Reviewers work.

Approve works.

Request Changes works.

Merge policy works.

Merge works.

Provider state refresh works.

Review state persists.

Audit works.

Review can be disabled cleanly.

The platform can review and merge development of itself.
```

---

# 62. Explicit deferrals to Phase 2

Do NOT implement these during Phase 1:

```text
Pipelines

Pipeline definitions

Pipeline runs

Jobs

Steps

Runners

Unit test execution

Integration test execution

E2E test execution

Checks

Test result parsing

Pipeline artifacts

Merge requirements based on checks
```

However, Phase 1 must preserve the extension points necessary for Phase 2 to add:

```text
Change
   ↓
Pipeline Trigger
   ↓
Pipeline Run
   ↓
Checks
   ↓
Review Merge Requirement
```

without rewriting the Review module.

---

# 63. Explicit deferrals to Phase 3

Do NOT implement:

```text
Deployments

Applications

Releases

Environments

Deployment targets

Deployment agents

Docker deployment

Promotion

Rollback
```

These belong to Phase 3.

---

# 64. Implementation order

Implement Phase 1 in this order:

```text
1. Project/local-repository association

2. Real GitHub authentication

3. GitHub source provider

4. Repository browser

5. Branch browser

6. Commit browser

7. Change provider abstraction

8. Existing PR discovery/import

9. Change creation

10. Change page

11. Diff rendering

12. General comments

13. Inline discussions

14. Reviewer assignment

15. Approve / Request Changes

16. Basic approval policy

17. Merge

18. Provider-state synchronisation

19. Activity timeline

20. Audit

21. Review queue

22. Dogfood the platform against itself
```

Do not proceed to Phase 2 until the dogfood acceptance workflow works reliably.

---

# 65. Guiding implementation rule

The implementation agent should optimise for:

> A developer can point the locally running platform at the repository they are currently working on and genuinely use it instead of GitHub's Pull Request UI.

Do not optimise Phase 1 for theoretical feature completeness.

Do not recreate GitHub.

Build the smallest complete Review product that is good enough to use every day while developing Phase 2.
