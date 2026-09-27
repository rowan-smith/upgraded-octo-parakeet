# Event Contract Catalogue

Canonical Module events for Git, Review, Build, and Deploy. Each entry uses the fields from the architecture specification (§36). Runtime registration via `IEventRegistry` remains authoritative; see also [events.md](events.md).

Diagnostics: `GET /api/platform/events/contracts` returns registered contracts from `IEventRegistry.List()`.

---

## Git

### GitRepositoryCreatedEvent

| Field | Value |
|-------|-------|
| Name | `GitRepositoryCreatedEvent` |
| Owner Module | Git |
| Stable Type | `forgedeck.git.repository-created` |
| Version | 1 |
| Purpose | A native ForgeDeck repository was created. |
| When published | After repository row and initial commit metadata are persisted. |
| Payload | `RepositoryId`, `ProjectKey`, `Repository` |
| Required fields | All payload fields |
| Actor semantics | Typically `System` / Git. |
| Organisation/Project scope | Optional envelope `OrganisationId` / `ProjectId`. |
| Correlation behaviour | New chain: `CorrelationId == Id`. |
| Causation behaviour | None (root). |
| Current publisher(s) | Git |
| Current subscriber(s) | None required |
| Delivery durability | Durable outbox (default) |
| Idempotency expectations | Deduplicate by Event ID; repository id is stable. |
| Replay/reconciliation | Not auto-replayed on module activation. |

### GitRepositoryPushEvent

| Field | Value |
|-------|-------|
| Name | `GitRepositoryPushEvent` |
| Owner Module | Git |
| Stable Type | `forgedeck.git.repository-push` |
| Version | 1 |
| Purpose | Commits were pushed to a branch. |
| When published | After push is accepted and ref tip updated. |
| Payload | `RepositoryId`, `ProjectKey`, `Repository`, `Branch`, `CommitSha`, `PreviousCommitSha?` |
| Required fields | All except `PreviousCommitSha` |
| Actor semantics | `User` or `External` (e.g. connector). |
| Organisation/Project scope | Optional envelope tenancy. |
| Correlation behaviour | New chain or inherited when caused by inbound sync. |
| Causation behaviour | May parent downstream Review/Build events. |
| Current publisher(s) | Git |
| Current subscriber(s) | Review, Build (when installed) |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID; consumers key on repo+sha. |
| Replay/reconciliation | Not auto-replayed on module activation. |

### GitRepositoryRefUpdatedEvent

| Field | Value |
|-------|-------|
| Name | `GitRepositoryRefUpdatedEvent` |
| Owner Module | Git |
| Stable Type | `forgedeck.git.repository-ref-updated` |
| Version | 1 |
| Purpose | A named ref tip changed. |
| When published | After non-push or administrative ref update. |
| Payload | `RepositoryId`, `ProjectKey`, `Repository`, `RefName`, `CommitSha` |
| Required fields | All |
| Actor semantics | `System` / `User` / `External` |
| Organisation/Project scope | Optional envelope tenancy |
| Correlation behaviour | Inherited when part of a sync workflow |
| Causation behaviour | May cause Review/Build work |
| Current publisher(s) | Git |
| Current subscriber(s) | Optional |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

### GitRepositoryDeletedEvent

| Field | Value |
|-------|-------|
| Name | `GitRepositoryDeletedEvent` |
| Owner Module | Git |
| Stable Type | `forgedeck.git.repository-deleted` |
| Version | 1 |
| Purpose | A repository was deleted. |
| When published | After delete succeeds. |
| Payload | `RepositoryId`, `ProjectKey`, `Repository` |
| Required fields | All |
| Actor semantics | `User` / `System` |
| Organisation/Project scope | Optional envelope tenancy |
| Correlation behaviour | New or inherited |
| Causation behaviour | Root or admin action |
| Current publisher(s) | Git |
| Current subscriber(s) | Optional cleanup handlers |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

---

## Review

### ReviewRequestedEvent

| Field | Value |
|-------|-------|
| Name | `ReviewRequestedEvent` |
| Owner Module | Review |
| Stable Type | `forgedeck.review.requested` |
| Version | 1 |
| Purpose | A change was opened for review. |
| When published | After change creation. |
| Payload | `ChangeId`, `ProjectKey`, `Repository`, `SourceBranch`, `TargetBranch`, `CommitSha`, `RepositoryUrl?` |
| Required fields | All except `RepositoryUrl` |
| Actor semantics | `User` |
| Organisation/Project scope | Project-scoped when set on envelope |
| Correlation behaviour | Often continues from `GitRepositoryPushEvent` |
| Causation behaviour | `CausationId` = parent push/update when applicable |
| Current publisher(s) | Review |
| Current subscriber(s) | Build (pipeline triggers), notifications |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID / `ChangeId` |
| Replay/reconciliation | Not auto-replayed |

### ReviewApprovedEvent

| Field | Value |
|-------|-------|
| Name | `ReviewApprovedEvent` |
| Owner Module | Review |
| Stable Type | `forgedeck.review.approved` |
| Version | 1 |
| Purpose | A reviewer approved a change. |
| When published | After approval is recorded. |
| Payload | `ChangeId`, `ProjectKey`, `ApproverId`, `ApproverDisplayName` |
| Required fields | All |
| Actor semantics | `User` (approver) |
| Organisation/Project scope | Optional envelope tenancy |
| Correlation behaviour | Inherited from review workflow |
| Causation behaviour | Caused by prior review activity |
| Current publisher(s) | Review |
| Current subscriber(s) | Optional |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

### ReviewChangesRequestedEvent

| Field | Value |
|-------|-------|
| Name | `ReviewChangesRequestedEvent` |
| Owner Module | Review |
| Stable Type | `forgedeck.review.changes-requested` |
| Version | 1 |
| Purpose | A reviewer requested changes. |
| When published | After changes-requested decision. |
| Payload | `ChangeId`, `ProjectKey`, `ReviewerId`, `Comment?` |
| Required fields | All except `Comment` |
| Actor semantics | `User` |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited |
| Causation behaviour | Parent review event |
| Current publisher(s) | Review |
| Current subscriber(s) | Optional |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

### ReviewCommentAddedEvent

| Field | Value |
|-------|-------|
| Name | `ReviewCommentAddedEvent` |
| Owner Module | Review |
| Stable Type | `forgedeck.review.comment-added` |
| Version | 1 |
| Purpose | A comment was added to a change. |
| When published | After comment persist. |
| Payload | `ChangeId`, `ProjectKey`, `AuthorId`, `Body` |
| Required fields | All |
| Actor semantics | `User` |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited |
| Causation behaviour | Parent review activity |
| Current publisher(s) | Review |
| Current subscriber(s) | Optional / notifications |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

### ReviewMergedEvent

| Field | Value |
|-------|-------|
| Name | `ReviewMergedEvent` |
| Owner Module | Review |
| Stable Type | `forgedeck.review.merged` |
| Version | 1 |
| Purpose | A change was merged. |
| When published | After merge completes. |
| Payload | `ChangeId`, `ProjectKey`, `CommitSha` |
| Required fields | All |
| Actor semantics | `User` / `System` |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited from review chain |
| Causation behaviour | May cause Deploy/Build follow-ups |
| Current publisher(s) | Review |
| Current subscriber(s) | Build, Deploy (optional) |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID / `ChangeId` |
| Replay/reconciliation | Not auto-replayed |

### ReviewRevisionUpdatedEvent

| Field | Value |
|-------|-------|
| Name | `ReviewRevisionUpdatedEvent` |
| Owner Module | Review |
| Stable Type | `forgedeck.review.revision-updated` |
| Version | 1 |
| Purpose | Source revision on an open change advanced. |
| When published | After new commits land on the change. |
| Payload | `ChangeId`, `ProjectKey`, `Repository`, `SourceBranch`, `TargetBranch`, `CommitSha`, `PreviousCommitSha`, `RepositoryUrl?` |
| Required fields | All except `RepositoryUrl` |
| Actor semantics | `System` / `External` / `User` |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited from push → review chain |
| Causation behaviour | Typically caused by `GitRepositoryPushEvent` |
| Current publisher(s) | Review |
| Current subscriber(s) | Build |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID; key on `ChangeId`+`CommitSha` |
| Replay/reconciliation | Not auto-replayed |

---

## Build

### BuildPipelineRunRequestedEvent

| Field | Value |
|-------|-------|
| Name | `BuildPipelineRunRequestedEvent` |
| Owner Module | Build |
| Stable Type | `forgedeck.build.pipeline-run-requested` |
| Version | 1 |
| Purpose | A pipeline execution was requested. |
| When published | On manual/API/push/review trigger before queue. |
| Payload | `DefinitionId`, `PipelineName`, `Branch`, `CommitSha`, `ChangeId?`, `RepositoryUrl?`, `Trigger?`, `IdempotencyKey?` |
| Required fields | `DefinitionId`, `PipelineName`, `Branch`, `CommitSha` |
| Actor semantics | `User` / `System` / `Extension` |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited from Git/Review when applicable |
| Causation behaviour | Parent push/revision/request event |
| Current publisher(s) | Build |
| Current subscriber(s) | Build (queue handler) |
| Delivery durability | Durable outbox |
| Idempotency expectations | Prefer `IdempotencyKey` / Event ID |
| Replay/reconciliation | Not auto-replayed |

### BuildPipelineRunQueuedEvent

| Field | Value |
|-------|-------|
| Name | `BuildPipelineRunQueuedEvent` |
| Owner Module | Build |
| Stable Type | `forgedeck.build.pipeline-run-queued` |
| Version | 1 |
| Purpose | A run was accepted into the queue. |
| When published | After run row is created as queued. |
| Payload | `RunId`, `DefinitionId`, `PipelineName`, `CommitSha`, `ChangeId?` |
| Required fields | All except `ChangeId` |
| Actor semantics | `System` / Build |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited |
| Causation behaviour | Caused by requested event |
| Current publisher(s) | Build |
| Current subscriber(s) | Review (activity), optional |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID / `RunId` |
| Replay/reconciliation | Not auto-replayed |

### BuildPipelineRunStartedEvent

| Field | Value |
|-------|-------|
| Name | `BuildPipelineRunStartedEvent` |
| Owner Module | Build |
| Stable Type | `forgedeck.build.pipeline-run-started` |
| Version | 1 |
| Purpose | A run began executing. |
| When published | When first job starts. |
| Payload | `RunId`, `ChangeId?`, `PipelineName`, `CommitSha` |
| Required fields | `RunId`, `PipelineName`, `CommitSha` |
| Actor semantics | `System` / runner |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited |
| Causation behaviour | Caused by queued |
| Current publisher(s) | Build |
| Current subscriber(s) | Review |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID / `RunId` |
| Replay/reconciliation | Not auto-replayed |

### BuildPipelineRunSucceededEvent

| Field | Value |
|-------|-------|
| Name | `BuildPipelineRunSucceededEvent` |
| Owner Module | Build |
| Stable Type | `forgedeck.build.pipeline-run-succeeded` |
| Version | 1 |
| Purpose | A pipeline run completed successfully. |
| When published | After terminal success. |
| Payload | `RunId`, `ChangeId?`, `PipelineName`, `CommitSha` |
| Required fields | `RunId`, `PipelineName`, `CommitSha` |
| Actor semantics | `System` |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited from initiating workflow |
| Causation behaviour | Parent started/queued chain |
| Current publisher(s) | Build |
| Current subscriber(s) | Review, Deploy |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed on module activation |

### BuildPipelineRunFailedEvent

| Field | Value |
|-------|-------|
| Name | `BuildPipelineRunFailedEvent` |
| Owner Module | Build |
| Stable Type | `forgedeck.build.pipeline-run-failed` |
| Version | 1 |
| Purpose | A pipeline run failed. |
| When published | After terminal failure. |
| Payload | `RunId`, `ChangeId?`, `PipelineName`, `CommitSha`, `Reason?` |
| Required fields | `RunId`, `PipelineName`, `CommitSha` |
| Actor semantics | `System` |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited |
| Causation behaviour | Parent run events |
| Current publisher(s) | Build |
| Current subscriber(s) | Review |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

### BuildPipelineRunCancelledEvent

| Field | Value |
|-------|-------|
| Name | `BuildPipelineRunCancelledEvent` |
| Owner Module | Build |
| Stable Type | `forgedeck.build.pipeline-run-cancelled` |
| Version | 1 |
| Purpose | A pipeline run was cancelled. |
| When published | After cancel is applied. |
| Payload | `RunId`, `ChangeId?`, `PipelineName`, `CommitSha` |
| Required fields | `RunId`, `PipelineName`, `CommitSha` |
| Actor semantics | `User` / `System` |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited |
| Causation behaviour | Parent run events |
| Current publisher(s) | Build |
| Current subscriber(s) | Review |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

### BuildCheckUpdatedEvent

| Field | Value |
|-------|-------|
| Name | `BuildCheckUpdatedEvent` |
| Owner Module | Build |
| Stable Type | `forgedeck.build.check-updated` |
| Version | 1 |
| Purpose | Build-originated check status changed for a change/commit. |
| When published | When run status maps to a check update. |
| Payload | `ChangeId?`, `RunId`, `PipelineName`, `CommitSha`, `Status` |
| Required fields | `RunId`, `PipelineName`, `CommitSha`, `Status` |
| Actor semantics | `System` / Extension |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited |
| Causation behaviour | Parent run events |
| Current publisher(s) | Build |
| Current subscriber(s) | Review (prefer also platform `CheckUpdatedEvent` where provider-neutral) |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID; status is last-write-wins per check key |
| Replay/reconciliation | Not auto-replayed |

### BuildArtifactProducedEvent

| Field | Value |
|-------|-------|
| Name | `BuildArtifactProducedEvent` |
| Owner Module | Build |
| Stable Type | `forgedeck.build.artifact-produced` |
| Version | 1 |
| Purpose | A build artifact is available. |
| When published | After artifact is stored/registered. |
| Payload | `RunId`, `PipelineName`, `ArtifactName`, `Uri?` |
| Required fields | `RunId`, `PipelineName`, `ArtifactName` |
| Actor semantics | `System` |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited |
| Causation behaviour | Parent succeeded/run events |
| Current publisher(s) | Build |
| Current subscriber(s) | Deploy |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID / run+artifact name |
| Replay/reconciliation | Not auto-replayed |

---

## Deploy

### DeployReleaseEvent

| Field | Value |
|-------|-------|
| Name | `DeployReleaseEvent` |
| Owner Module | Deploy |
| Stable Type | `forgedeck.deploy.release` |
| Version | 1 |
| Purpose | A release was requested for an environment. |
| When published | On release API / promotion. |
| Payload | `ReleaseId`, `EnvironmentId`, `ProjectKey`, `RequestedBy?` |
| Required fields | `ReleaseId`, `EnvironmentId`, `ProjectKey` |
| Actor semantics | `User` / `System` |
| Organisation/Project scope | Optional |
| Correlation behaviour | New or inherited |
| Causation behaviour | May parent queued/started chain |
| Current publisher(s) | Deploy |
| Current subscriber(s) | Deploy handlers |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID / `ReleaseId` |
| Replay/reconciliation | Not auto-replayed |

### DeployRequestedEvent

| Field | Value |
|-------|-------|
| Name | `DeployRequestedEvent` |
| Owner Module | Deploy |
| Stable Type | `forgedeck.deploy.requested` |
| Version | 1 |
| Purpose | A deployment was requested. |
| When published | Before queue when request semantics are explicit. |
| Payload | `DeploymentId`, `ReleaseId`, `EnvironmentId`, `ProjectKey` |
| Required fields | All |
| Actor semantics | `User` / `System` |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited |
| Causation behaviour | Parent release |
| Current publisher(s) | Deploy |
| Current subscriber(s) | Deploy |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

### DeployQueuedEvent

| Field | Value |
|-------|-------|
| Name | `DeployQueuedEvent` |
| Owner Module | Deploy |
| Stable Type | `forgedeck.deploy.queued` |
| Version | 1 |
| Purpose | Deployment accepted as Pending and queued for execution. |
| When published | After Pending save in `CreateDeployment`. |
| Payload | `DeploymentId`, `ReleaseId`, `EnvironmentId` |
| Required fields | All |
| Actor semantics | Extension `forgedeck.deploy` |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited / new |
| Causation behaviour | Parent request/release |
| Current publisher(s) | Deploy |
| Current subscriber(s) | Optional agents (Phase H) |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID / `DeploymentId` |
| Replay/reconciliation | Not auto-replayed |

### DeployStartedEvent

| Field | Value |
|-------|-------|
| Name | `DeployStartedEvent` |
| Owner Module | Deploy |
| Stable Type | `forgedeck.deploy.started` |
| Version | 1 |
| Purpose | Executor began the deployment. |
| When published | From `IDeploymentExecutor` on start. |
| Payload | `DeploymentId`, `ReleaseId`, `EnvironmentId` |
| Required fields | All |
| Actor semantics | Extension / System |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited |
| Causation behaviour | Caused by queued |
| Current publisher(s) | Deploy (`ImmediateDeploymentExecutor` default) |
| Current subscriber(s) | Optional |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

### DeploySucceededEvent

| Field | Value |
|-------|-------|
| Name | `DeploySucceededEvent` |
| Owner Module | Deploy |
| Stable Type | `forgedeck.deploy.succeeded` |
| Version | 1 |
| Purpose | Deployment completed successfully. |
| When published | From executor on success. |
| Payload | `DeploymentId`, `ReleaseId`, `EnvironmentId` |
| Required fields | All |
| Actor semantics | Extension / System |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited |
| Causation behaviour | Parent started |
| Current publisher(s) | Deploy |
| Current subscriber(s) | Notifications / projections (future) |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

### DeployFailedEvent

| Field | Value |
|-------|-------|
| Name | `DeployFailedEvent` |
| Owner Module | Deploy |
| Stable Type | `forgedeck.deploy.failed` |
| Version | 1 |
| Purpose | Deployment failed. |
| When published | On executor failure or `FailDeployment`. |
| Payload | `DeploymentId`, `ReleaseId`, `EnvironmentId`, `Reason?` |
| Required fields | All except `Reason` |
| Actor semantics | Extension / System |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited |
| Causation behaviour | Parent started/queued |
| Current publisher(s) | Deploy |
| Current subscriber(s) | Optional |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

### DeployRollbackRequestedEvent

| Field | Value |
|-------|-------|
| Name | `DeployRollbackRequestedEvent` |
| Owner Module | Deploy |
| Stable Type | `forgedeck.deploy.rollback-requested` |
| Version | 1 |
| Purpose | Rollback to a target release was requested. |
| When published | On rollback request API/handler. |
| Payload | `EnvironmentId`, `TargetReleaseId`, `ProjectKey` |
| Required fields | All |
| Actor semantics | `User` / `System` |
| Organisation/Project scope | Optional |
| Correlation behaviour | New or inherited |
| Causation behaviour | Admin/user action |
| Current publisher(s) | Deploy |
| Current subscriber(s) | Deploy rollback handler |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

### DeployRolledBackEvent

| Field | Value |
|-------|-------|
| Name | `DeployRolledBackEvent` |
| Owner Module | Deploy |
| Stable Type | `forgedeck.deploy.rolled-back` |
| Version | 1 |
| Purpose | Environment was rolled back to a prior release. |
| When published | After rollback deployment is recorded. |
| Payload | `DeploymentId`, `EnvironmentId`, `ReleaseId` |
| Required fields | All |
| Actor semantics | Extension / User |
| Organisation/Project scope | Optional |
| Correlation behaviour | Inherited from rollback request |
| Causation behaviour | Parent rollback-requested |
| Current publisher(s) | Deploy |
| Current subscriber(s) | Optional |
| Delivery durability | Durable outbox |
| Idempotency expectations | Deduplicate by Event ID |
| Replay/reconciliation | Not auto-replayed |

---

## Dependency graph (summary)

```text
GitRepositoryPushEvent
├── Review
└── Build

ReviewRevisionUpdatedEvent
└── Build

BuildCheckUpdatedEvent
└── Review

BuildArtifactProducedEvent / BuildPipelineRunSucceededEvent
└── Deploy

DeploySucceededEvent
├── Notifications (future)
└── dashboard projections (future)
```
