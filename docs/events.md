# Feature Specification — ForgeDeck Universal Event Architecture

## 1. Objective

Implement a universal event-driven architecture for ForgeDeck where **events are the primary mechanism for causing work and communicating state changes between independently installable Modules and Connectors**.

The architecture exists primarily to preserve module independence.

Core must not need to understand:

```text
Pull Requests
Pipelines
Build Runs
Releases
Deployments
Repositories
Review State
Artifacts
```

in order to coordinate those features.

Instead:

```text
Something requests work
        ↓
Event
        ↓
Interested Module handles it
        ↓
Module changes its own state
        ↓
Module publishes another Event
        ↓
Other interested Modules react
```

The central rule is:

> **Core owns event infrastructure. Modules own event semantics.**

ForgeDeck does not introduce separate architectural primitives for:

```text
Commands
User Events
System Events
External Events
Integration Events
```

They all use the same Event infrastructure.

Semantic differences are expressed by:

- Event type.
- Event name.
- Event payload.
- Actor metadata.
- Correlation and causation.
- The Module that owns the Event.

---

# 2. High-level architecture

```text
                         FORGEDECK CORE

┌────────────────────────────────────────────────────────────┐
│ Organisation                                               │
│ Users / Teams / Projects                                   │
│ Permissions                                                │
│ Licensing                                                  │
│ Extension Runtime                                          │
│                                                            │
│ Event Infrastructure                                       │
│ ├── Event Bus                                              │
│ ├── Event Registry                                         │
│ ├── Handler Registry                                       │
│ ├── Transactional Outbox                                   │
│ ├── Consumer Inbox                                         │
│ ├── Retry / Failure Handling                               │
│ ├── Correlation / Causation                                │
│ └── Diagnostics                                            │
└─────────────────────────────┬──────────────────────────────┘
                              │
                         opaque events
                              │
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
        ▼                     ▼                     ▼
       Git                   Review                Build
        │                     │                     │
        │                     │                     │
        └────────────── events ┼ events ────────────┘
                              │
                              ▼
                            Deploy
```

Core transports Events without knowing what the Module-specific payload means.

---

# 3. Architectural principles

ForgeDeck event architecture MUST follow these principles:

```text
Everything that requests asynchronous/module work may be represented by an Event.

Something that happened may be represented by an Event.

Modules communicate with other Modules through Events or narrow provider/query contracts.

Modules must never directly invoke another optional Module's implementation.

Core must contain no Build-, Review-, Deploy-, Code-, or Git-specific event logic.

Events may have zero subscribers.

Events may have one subscriber.

Events may have many subscribers.

Event handlers may publish additional Events.

Events are immutable after publication.

Delivery is at-least-once.

Handlers must therefore be idempotent.

Events coordinate state but are not the authoritative state store.

ForgeDeck is event-driven, not necessarily event-sourced.
```

---

# 4. Event versus query

Use Events when:

```text
Something should happen.

Something happened.

Another Module might care about something that happened.
```

Use a provider/query contract when:

```text
Current information is required synchronously.
```

Example:

```text
Repository changed
        → Event

Build completed
        → Event

Deploy requested
        → Event

Get file contents
        → ISourceProvider

Get current checks
        → ICheckProvider

Get artifact
        → IArtifactProvider
```

Do not use Events as a request/response database-query mechanism.

---

# 5. Core must remain Module-neutral

Core may know:

```text
Event ID
Event type
Event version
Event payload bytes/JSON
Actor
Organisation
Project
Timestamp
Correlation ID
Causation ID
Publisher
Delivery state
```

Core MUST NOT contain fields such as:

```text
PipelineId
PullRequestId
BuildStatus
ReleaseId
DeploymentStatus
ReviewStatus
```

Those belong to Module-owned payloads.

---

# 6. Event naming rule

All Module-owned Event class/type names follow this convention:

```text
<ModuleName><SemanticName>Event
```

Examples:

```text
DeployReleaseEvent

ReviewRequestedEvent

ReviewApprovedEvent

BuildPipelineRunRequestedEvent

BuildPipelineRunStartedEvent

BuildPipelineRunSucceededEvent

GitRepositoryPushEvent

CodeRepositoryViewedEvent
```

The first token MUST identify the Module that owns/publishes the Event.

The name MUST end with:

```text
Event
```

---

# 7. Naming is a development convention, not physical enforcement

This rule is deliberately **not physically enforced**.

Do NOT add:

```text
Roslyn analyzers

runtime naming validation

reflection-based startup rejection

build scripts rejecting non-prefixed types

base classes tied to Module names
```

solely to enforce Event naming.

Instead, document this as a known ForgeDeck engineering rule.

The reasoning is:

> Event naming communicates ownership to developers, but Module ownership should be defined by architecture and registration rather than inferred from a class name.

Code review and development conventions enforce the rule socially.

---

# 8. Event naming examples

Review:

```text
ReviewRequestedEvent

ReviewApprovedEvent

ReviewChangesRequestedEvent

ReviewCommentAddedEvent

ReviewMergedEvent

ReviewRevisionUpdatedEvent
```

Build:

```text
BuildPipelineRunRequestedEvent

BuildPipelineRunQueuedEvent

BuildPipelineRunStartedEvent

BuildPipelineRunSucceededEvent

BuildPipelineRunFailedEvent

BuildPipelineRunCancelledEvent

BuildCheckUpdatedEvent

BuildArtifactProducedEvent
```

Deploy:

```text
DeployReleaseEvent

DeployRequestedEvent

DeployQueuedEvent

DeployStartedEvent

DeploySucceededEvent

DeployFailedEvent

DeployRollbackRequestedEvent

DeployRolledBackEvent
```

Git:

```text
GitRepositoryCreatedEvent

GitRepositoryPushEvent

GitRepositoryRefUpdatedEvent

GitRepositoryDeletedEvent
```

Code may publish relatively few Events because much of Code is read-oriented.

Possible examples:

```text
CodeRepositoryIndexedEvent

CodeSearchIndexUpdatedEvent
```

Do not manufacture Events merely to make every Module publish them.

---

# 9. Request semantics

The event architecture does not introduce a separate Command abstraction.

A request for work is simply represented by an appropriately named Event.

Example:

```text
BuildPipelineRunRequestedEvent
```

means:

> A pipeline execution has been requested.

It does not mean:

> A pipeline execution has started.

That distinction is represented by subsequent Events:

```text
BuildPipelineRunRequestedEvent
        ↓
BuildPipelineRunQueuedEvent
        ↓
BuildPipelineRunStartedEvent
        ↓
BuildPipelineRunSucceededEvent
```

or:

```text
BuildPipelineRunFailedEvent
```

---

# 10. User-triggered actions

A user action may cause an Event.

Example:

```text
Rowan clicks
Run Pipeline

        ↓

BuildPipelineRunRequestedEvent

Actor
User / Rowan
```

The Event itself uses the same infrastructure as an automatically generated request.

---

# 11. System-triggered actions

Example:

```text
Schedule reaches 02:00

        ↓

BuildPipelineRunRequestedEvent

Actor
System / Scheduler
```

Same Event.

Different Actor.

Handlers do not need separate implementations merely because the request originated from a different source.

---

# 12. External actions

Example GitHub flow:

```text
GitHub webhook
      ↓
GitHub Connector
      ↓
Verify webhook
      ↓
Resolve Repository
      ↓
Publish
GitRepositoryPushEvent
```

Actor:

```text
External / GitHub
```

Again, no separate "external event" infrastructure exists.

---

# 13. Actor

Every Event should contain an Actor.

Conceptually:

```csharp
public enum ActorType
{
    User,
    System,
    Extension,
    External
}
```

Example:

```csharp
public sealed record EventActor(
    ActorType Type,
    string? Id,
    string? DisplayName);
```

Examples:

```text
User
rowan

System
pipeline-scheduler

Extension
forgedeck.build

External
github
```

---

# 14. Event envelope

Use one transport-neutral envelope.

Conceptually:

```csharp
public sealed record EventEnvelope(
    Guid Id,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    EventActor Actor,
    Guid CorrelationId,
    Guid? CausationId,
    Guid? OrganisationId,
    Guid? ProjectId,
    string Publisher,
    JsonElement Data);
```

A strongly typed generic representation MAY be used internally:

```csharp
public sealed record EventEnvelope<TEvent>(
    Guid Id,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    EventActor Actor,
    Guid CorrelationId,
    Guid? CausationId,
    Guid? OrganisationId,
    Guid? ProjectId,
    string Publisher,
    TEvent Data);
```

Transport persistence must not require Core to know `TEvent`.

---

# 15. Event ID

Every published Event receives a globally unique ID.

Example:

```text
evt_01K...
```

or:

```text
UUID/ULID
```

The Event ID is used for:

```text
deduplication

diagnostics

causation

delivery tracking
```

---

# 16. Event type

Events require a stable machine-readable Event Type independent of CLR class names.

Example:

```text
forgedeck.review.requested

forgedeck.review.approved

forgedeck.build.pipeline-run-requested

forgedeck.deploy.succeeded
```

Event class names may change internally without changing the published contract unnecessarily.

---

# 17. Event version

Each Event contract declares a contract version.

Example:

```text
Type
forgedeck.build.pipeline-run-started

Version
1
```

Do not use assembly version as the Event contract version.

---

# 18. Contract evolution

Additive compatible payload changes MAY remain at the same contract version according to documented compatibility rules.

Breaking changes MUST introduce a new version.

Example:

```text
forgedeck.build.pipeline-run-started
v1

forgedeck.build.pipeline-run-started
v2
```

Independently installed Modules may run compatible but different package versions, making explicit Event contract versions necessary.

---

# 19. Correlation

Events belonging to the same logical workflow share:

```text
CorrelationId
```

Example:

```text
GitRepositoryPushEvent
correlation = X

ReviewRevisionUpdatedEvent
correlation = X

BuildPipelineRunRequestedEvent
correlation = X

BuildPipelineRunQueuedEvent
correlation = X

BuildPipelineRunSucceededEvent
correlation = X
```

---

# 20. Causation

An Event caused by another Event references:

```text
CausationId
```

Example:

```text
GitRepositoryPushEvent
ID = A

ReviewRevisionUpdatedEvent
ID = B
Causation = A

BuildPipelineRunRequestedEvent
ID = C
Causation = B

BuildPipelineRunQueuedEvent
ID = D
Causation = C
```

This enables workflow tracing without introducing separate command tracking infrastructure.

---

# 21. New correlation chains

If an Event begins a new workflow and has no upstream Event:

```text
CorrelationId = EventId
CausationId = null
```

or equivalent deterministic creation behaviour.

---

# 22. Event ownership

Every Event has one owning publisher domain.

Example:

```text
ReviewApprovedEvent
Owner = Review

BuildPipelineRunStartedEvent
Owner = Build

DeploySucceededEvent
Owner = Deploy
```

Other Modules may consume the Event but do not redefine its meaning.

---

# 23. Contract ownership

Module Event contracts live with the Module.

Example:

```text
src/ce/modules/Review/
├── ForgeDeck.Review.Contracts/
│   └── Events/
│       ├── ReviewRequestedEvent.cs
│       ├── ReviewApprovedEvent.cs
│       └── ReviewMergedEvent.cs
```

Build:

```text
src/ce/modules/Build/
├── ForgeDeck.Build.Contracts/
│   └── Events/
```

Deploy:

```text
src/ce/modules/Deploy/
├── ForgeDeck.Deploy.Contracts/
│   └── Events/
```

Core must not define these contracts.

---

# 24. Cross-Module contract dependencies

Avoid requiring Module A to directly reference all of Module B's implementation merely to consume B's Events.

Permitted approaches include:

```text
small *.Contracts assembly

extension contract package

event schema generated from manifest
```

For example:

```text
ForgeDeck.Review
    references
ForgeDeck.Build.Contracts
```

may be acceptable where Review explicitly integrates with Build.

However, prefer platform-neutral Event contracts where multiple producers may exist.

---

# 25. Generic cross-provider Events

Where a concept has multiple possible producers, prefer a neutral contract.

Example Build-specific:

```text
BuildPipelineRunSucceededEvent
```

may be useful for Build-specific consumers.

But Review should ideally consume:

```text
CheckUpdatedEvent
```

from a neutral Checks contract if checks can originate from:

```text
ForgeDeck Build

GitHub Actions

Jenkins

GitLab CI
```

Avoid coupling Review exclusively to ForgeDeck Build when the domain is actually provider-neutral.

---

# 26. Event registration

Enabled Modules register Event handlers during extension activation.

Conceptually:

```csharp
extension.Events
    .Subscribe<GitRepositoryPushEvent, RepositoryPushHandler>()
    .Subscribe<BuildPipelineRunRequestedEvent, PipelineRequestHandler>();
```

Core owns the registration mechanism.

The Module owns the handlers.

---

# 27. Publication registration

Modules may optionally declare Events they publish for:

```text
documentation

event catalogue

diagnostics

compatibility checks
```

Publication declaration must not prevent a Module from functioning if an Event has zero subscribers.

---

# 28. Module activation

When a Module becomes enabled:

```text
Load Module
    ↓
Register Event contracts
    ↓
Register Event handlers
    ↓
Register publishers
    ↓
Start Module background services
    ↓
Module active
```

Only then may new Events be delivered to its handlers.

---

# 29. Module disablement

When a Module is disabled:

```text
Stop accepting new Module work
        ↓
Drain or stop handlers safely
        ↓
Unregister subscriptions
        ↓
Stop Module workers
        ↓
Disable Module
```

Other Modules continue publishing Events.

No special handling is required when an Event has no interested subscribers.

---

# 30. Disabled subscriber behaviour

Given:

```text
Build enabled
Review disabled
```

Build publishes:

```text
BuildCheckUpdatedEvent
```

No Review handler runs.

This MUST NOT cause Build publication failure.

---

# 31. Module re-enable behaviour

Re-enabling Review does NOT automatically mean:

```text
Replay every historical Build Event
```

Review should re-establish current state through appropriate provider/query contracts if reconciliation is necessary.

Then future Events keep the projection current.

---

# 32. Event replay

Replay is explicit functionality.

Do not automatically replay all Events when:

```text
Module installed

Module enabled

Module updated
```

A Module may provide a reconciliation operation if historical state needs rebuilding.

---

# 33. Relational state remains authoritative

ForgeDeck is not initially event sourced.

Example:

```text
review.changes

build.pipeline_runs

deploy.deployments
```

remain authoritative Module state.

Events coordinate Modules and asynchronous behaviour.

---

# 34. Transactional Outbox

Publishing an Event as part of a Module state mutation MUST support transactional reliability.

Example:

```text
BEGIN

INSERT build.pipeline_runs

INSERT core.event_outbox

COMMIT
```

The Event dispatcher subsequently delivers the Outbox Event.

This prevents:

```text
PipelineRun persisted
but
PipelineRunQueuedEvent lost
```

---

# 35. Outbox ownership

Core provides generic Outbox infrastructure.

The Outbox record contains:

```text
EventId

EventType

Version

Envelope

Payload

CreatedAt

DispatchState

Attempts

LastAttemptAt

LastError
```

Core does not inspect Module payload semantics.

---

# 36. Consumer Inbox

Each durable consumer must support message deduplication.

Conceptually:

```text
EventId
ConsumerId
ProcessedAt
```

Before processing:

```text
Has EventId already been successfully processed by this Consumer?

Yes
    → do nothing

No
    → process
```

---

# 37. Delivery guarantee

ForgeDeck guarantees:

```text
at-least-once
```

delivery where durable event handling is configured.

ForgeDeck does NOT promise:

```text
exactly-once transport
```

Instead:

```text
at-least-once delivery
+
idempotent consumers
```

produces reliable behaviour.

---

# 38. Idempotent handlers

Every durable handler MUST be safe when the same Event arrives multiple times.

Example:

```text
BuildPipelineRunRequestedEvent
```

delivered twice must not create two Runs where only one logical Run is intended.

Use:

```text
Event ID

Business idempotency key

Unique constraint

Inbox record
```

as appropriate.

---

# 39. Event retries

Transient handler failure:

```text
Attempt 1
Failed

Attempt 2
Failed

Attempt 3
Succeeded
```

must not cause Event loss.

Retry policy should support:

```text
maximum attempts

backoff

last error

next retry
```

---

# 40. Permanent failures

After the configured retry policy:

```text
Event delivery = Failed
```

The Event should enter a failed/dead-letter state.

Administration/diagnostics must allow:

```text
View Failure

Retry Delivery

Ignore/Acknowledge
```

where safe.

---

# 41. Failure isolation

A broken subscriber must not prevent unrelated subscribers from processing the same Event.

Example:

```text
BuildPipelineRunFailedEvent
        │
        ├── Review handler       succeeds
        ├── Notification handler fails
        └── Audit handler        succeeds
```

Notification failure must not roll back successful independent consumers.

---

# 42. Subscription identity

Each subscriber should have a stable ID.

Example:

```text
forgedeck.review.build-check-projection

forgedeck.notifications.build-failure
```

This identity is used for:

```text
Inbox deduplication

Diagnostics

Retry tracking

Subscription management
```

---

# 43. Event ordering

Do not assume global Event ordering.

Ordering MAY be maintained where necessary within a defined stream/resource.

Example:

```text
Pipeline Run 184

Queued
Started
Succeeded
```

Handlers should use:

```text
resource version

timestamp

sequence

state transition rules
```

where stale/out-of-order Events could be harmful.

---

# 44. Stale Events

Example:

```text
BuildCheckUpdatedEvent

Commit
abc123
```

must not update a Review check projection for:

```text
current revision
def456
```

without preserving the correct Revision association.

Events should carry stable resource identities and revision information where semantically required.

---

# 45. Event loops

Modules must avoid accidental loops.

Example of unwanted behaviour:

```text
BuildCheckUpdatedEvent
    ↓
Review updates projection
    ↓
ReviewUpdatedEvent
    ↓
Build triggers build
    ↓
BuildCheckUpdatedEvent
```

Subscriptions should react only to semantic Events that genuinely require work.

Projection/database updates should not automatically publish Events unless a meaningful domain fact occurred.

---

# 46. Event handlers should be narrow

Prefer:

```text
RepositoryPushHandler

PipelineRunRequestHandler

CheckProjectionHandler
```

over one giant:

```text
BuildEventHandler
```

that switches on many Event types.

This improves testability and Module isolation.

---

# 47. Git push flow

Example:

```text
Git push
     ↓
Git Module / Connector
     ↓
GitRepositoryPushEvent
     │
     ├────────────────────┐
     ▼                    ▼
   Review                Build
     │                    │
     ▼                    ▼
ReviewRevision       Evaluate triggers
UpdatedEvent              │
                          ▼
             BuildPipelineRunRequestedEvent
                          │
                          ▼
              BuildPipelineRunQueuedEvent
```

No Core logic understands any of these states.

---

# 48. Manual Build flow

```text
User clicks Run
      ↓
Build endpoint
      ↓
Authenticate
      ↓
Authorize build.run
      ↓
Publish
BuildPipelineRunRequestedEvent
      ↓
Build request subscriber
      ↓
Create Run
      ↓
BuildPipelineRunQueuedEvent
      ↓
Runner dispatch
      ↓
BuildPipelineRunStartedEvent
      ↓
...
```

There is no separate command infrastructure.

---

# 49. Review flow

Example:

```text
User requests review
      ↓
ReviewRequestedEvent
      ↓
Review creates assignment/state
      ↓
Notification subscriber
      ↓
Notification sent
```

Approval:

```text
User approves
      ↓
ReviewApprovedEvent
      │
      ├── Review updates approval state
      ├── Audit records action
      └── Merge policy projection reevaluates
```

Depending on implementation, events may be persisted transactionally alongside the state they represent.

---

# 50. Deploy flow

Example:

```text
DeployReleaseEvent
      ↓
Deploy subscriber
      ↓
Validate Release
Validate Environment
Validate permission/context
      ↓
Create Deployment
      ↓
DeployQueuedEvent
      ↓
Deployment Agent
      ↓
DeployStartedEvent
      ↓
DeploySucceededEvent
```

or:

```text
DeployFailedEvent
```

---

# 51. Authorization

A user-facing endpoint must perform permission checks before publishing actionable Events where practical.

Example:

```text
POST /build/pipelines/123/run

Authenticate
     ↓
Check Project access
     ↓
Check build.run
     ↓
Publish BuildPipelineRunRequestedEvent
```

Unauthorized request:

```text
403
```

and appropriate Audit entry.

Do not publish an actionable Event that bypasses authorization merely because "everything is an Event."

---

# 52. Internal Event authorization

Events generated by trusted Modules are not treated as equivalent to arbitrary user input.

However, handlers must still validate:

```text
resource exists

scope is correct

Module configuration permits operation

licence capability is available

state transition is legal
```

where appropriate.

---

# 53. Licensing

Event handlers must enforce relevant capability licensing.

Example:

```text
DeployReleaseEvent
      ↓
Deploy handler
      ↓
Capability required?
Deploy.AdvancedApproval
      ↓
Evaluate active licence
```

Core handles capability infrastructure.

Deploy owns the decision about which Deploy capability is required.

---

# 54. Audit

Audit infrastructure may observe Events.

However:

```text
Event history
```

and:

```text
Audit history
```

are separate concepts.

Not every low-level Event needs a user-facing audit record.

Modules may register formatters or audit mappings.

Example:

```text
ReviewApprovedEvent
      ↓
"Rowan approved PR #184"
```

---

# 55. Event diagnostics

Organisation administrators should eventually be able to inspect Event infrastructure.

Recommended internal/admin diagnostic page:

```text
Settings
    System
        Events
```

This page is operational rather than ordinary user workflow.

Display:

```text
Recent Events

Failed Deliveries

Subscriptions

Outbox

Consumer Health
```

---

# 56. Event trace

Given a Correlation ID, provide a trace such as:

```text
Repository push                        22:14:01
└─ Review revision updated             22:14:01
   └─ Build pipeline requested         22:14:01
      └─ Build pipeline queued         22:14:02
         └─ Build pipeline started     22:14:04
            └─ Build succeeded         22:15:19
```

This is diagnostic infrastructure and may be deferred beyond the first implementation, but correlation data must be captured from the beginning.

---

# 57. Module event catalogue

Module details may expose a developer/admin view:

```text
Build

Consumes
GitRepositoryPushEvent
ReviewRevisionUpdatedEvent
BuildPipelineRunRequestedEvent

Produces
BuildPipelineRunQueuedEvent
BuildPipelineRunStartedEvent
BuildPipelineRunSucceededEvent
BuildPipelineRunFailedEvent
BuildArtifactProducedEvent
```

This provides excellent visibility into Module integration without creating direct dependencies.

---

# 58. Extension manifest metadata

An extension manifest MAY declare Event contracts.

Example:

```json
{
  "id": "forgedeck.build",
  "events": {
    "consumes": [
      "forgedeck.git.repository-push"
    ],
    "produces": [
      "forgedeck.build.pipeline-run-queued",
      "forgedeck.build.pipeline-run-started",
      "forgedeck.build.pipeline-run-succeeded"
    ]
  }
}
```

Runtime registration remains authoritative.

Manifest metadata primarily supports:

```text
discovery

documentation

compatibility diagnostics
```

---

# 59. Core package structure

Recommended:

```text
src/ce/core/
├── ForgeDeck.Core/
├── ForgeDeck.Extensibility/
├── ForgeDeck.Messaging/
└── ForgeDeck.Contracts/
```

`ForgeDeck.Messaging` owns:

```text
IEventBus

IEventPublisher

IEventSubscriber

EventEnvelope

EventActor

CorrelationContext

Outbox

Inbox

Retry infrastructure

Serialization

Event registry
```

---

# 60. Module package structure

Example Build:

```text
src/ce/modules/Build/
├── ForgeDeck.Build.Contracts/
│   └── Events/
├── ForgeDeck.Build.Domain/
├── ForgeDeck.Build.Application/
├── ForgeDeck.Build.Infrastructure/
└── ForgeDeck.Build.Web/
```

Review and Deploy follow the same pattern.

---

# 61. Event handlers and transactions

A handler that changes Module state and publishes another Event should use:

```text
Module transaction
     ↓
state mutation
+
Outbox insertion
     ↓
COMMIT
```

Do not:

```text
update database
commit
publish directly
```

when loss between those operations would leave the system inconsistent.

---

# 62. Event serialization

Persist Events using a stable transport representation.

JSON is acceptable initially.

Serialization MUST be based on:

```text
Event Type
+
Event Version
```

rather than assembly-qualified CLR type names.

This avoids making persisted Events dependent on:

```text
namespace

assembly version

class relocation
```

---

# 63. Unknown Event types

Core encountering an unknown Event type must not crash.

Depending on context:

```text
no subscribers
→ Event considered deliverable/no-op

subscriber expects unsupported version
→ subscription delivery failure / compatibility issue
```

Core itself does not need to deserialize Module payloads.

---

# 64. Module uninstall

Before uninstalling a Module, pending Events targeting its consumers should be handled deterministically.

Possible states:

```text
processed

cancelled because consumer removed

retained for diagnostics
```

Do not retain indefinitely retrying deliveries to a Module that has been intentionally uninstalled.

---

# 65. Module update

Module update must preserve compatibility with Events already present in:

```text
Outbox

retry queue

consumer inbox
```

A new Module version must support pending compatible Event contract versions or explicitly migrate/handle them.

---

# 66. Safe Mode

ForgeDeck Safe Mode should disable optional Module handlers.

Core Event infrastructure remains accessible for diagnostics.

Admins can inspect:

```text
Failed Events

Failed Module subscriptions

Outbox backlog
```

without activating the broken Module.

---

# 67. Performance

The initial architecture may use:

```text
PostgreSQL Outbox

background dispatcher

in-process handler execution
```

No external broker is required initially.

Design interfaces so a future transport can use:

```text
RabbitMQ

NATS

Kafka

Azure Service Bus
```

without changing Event semantics.

---

# 68. External broker non-goal

Do not introduce distributed messaging solely because the architecture is event-driven.

ForgeDeck initially remains a modular monolith.

Reliability matters more than distribution.

---

# 69. Testing strategy

Testing must cover four major concerns:

```text
Event semantics

Event infrastructure

Module isolation

Full workflow behaviour
```

Testing layers:

```text
Unit

Integration

E2E
```

The following cases are minimum expectations rather than an exhaustive upper bound.

---

# Unit Testing

## 70. Unit — Event envelope creation

**Given**

a Module publishes an Event without existing correlation context.

**When**

the Event envelope is created.

**Then**

verify:

```text
Event ID generated

OccurredAt set

CorrelationId created

CausationId null

Actor preserved

OrganisationId preserved

ProjectId preserved

Type preserved

Version preserved
```

---

# 71. Unit — Child Event correlation

**Given**

Handler is processing Event A.

**When**

it publishes Event B.

**Then**

verify:

```text
B.CorrelationId = A.CorrelationId

B.CausationId = A.Id
```

---

# 72. Unit — Nested causation

Given:

```text
A → B → C
```

verify:

```text
A.Correlation = X

B.Correlation = X
B.Causation = A

C.Correlation = X
C.Causation = B
```

---

# 73. Unit — Actor preservation

Given a User-generated request:

```text
Actor
User / Rowan
```

verify downstream Event creation can preserve or deliberately replace Actor according to publisher semantics.

If Build automatically publishes:

```text
BuildPipelineRunQueuedEvent
```

it may use:

```text
Actor
Extension / forgedeck.build
```

while correlation preserves the initiating workflow.

Define and test the selected convention consistently.

---

# 74. Unit — Event immutability

Verify Event envelope and Event payload records cannot be mutated after creation through the public API.

---

# 75. Unit — Event serialization

For every public Event contract:

```text
serialize
deserialize
```

and assert payload equivalence.

---

# 76. Unit — Contract version

Verify every Event registration specifies:

```text
Type
Version > 0
```

Do not infer contract version from assembly metadata.

---

# 77. Unit — Unknown payload field compatibility

Given a v1 Event containing an additional unknown compatible field:

```text
consumer can deserialize known fields
```

where the compatibility policy allows additive fields.

---

# 78. Unit — Required Event field validation

Verify Module-specific handler validation rejects semantically malformed payloads such as:

```text
empty Pipeline ID

missing Repository ID

missing Revision
```

where required.

This validation belongs to the owning Module, not Core.

---

# 79. Unit — Event naming convention documentation

There must NOT be a unit test that automatically rejects classes because they do not begin with the Module name.

Instead verify naming conventions through normal code review.

Example accepted developer convention:

```text
ReviewApprovedEvent
DeployReleaseEvent
BuildPipelineRunStartedEvent
```

This explicitly preserves the requirement that naming is known but not physically enforced.

---

# 80. Unit — Event bus zero subscribers

Publish Event with no subscribers.

Verify:

```text
publication succeeds

no exception

no Module-specific fallback required
```

---

# 81. Unit — Event bus one subscriber

Publish Event.

Verify registered subscriber receives it exactly once during the single dispatch attempt.

---

# 82. Unit — Event bus multiple subscribers

Register three subscribers.

Publish one Event.

Verify all three are scheduled/processed independently.

---

# 83. Unit — Subscriber failure isolation

Subscriber A succeeds.

Subscriber B throws.

Subscriber C succeeds.

Verify:

```text
A success retained

B marked failed/retryable

C success retained
```

---

# 84. Unit — Handler idempotency

Deliver the same Event twice to a representative handler.

Verify domain side effect occurs once.

Example:

```text
BuildPipelineRunRequestedEvent
```

must create one logical Pipeline Run for that Event/idempotency context.

---

# 85. Unit — Inbox deduplication

Given:

```text
EventId A
Consumer B
```

already marked successfully processed.

Re-deliver A to B.

Verify handler is skipped.

---

# 86. Unit — Different consumers process same Event

Given Event A is processed by Consumer B.

When Consumer C receives A.

Verify C still processes it.

Inbox deduplication key is:

```text
EventId + ConsumerId
```

not Event ID alone.

---

# 87. Unit — Retry scheduling

Handler throws transient exception.

Verify:

```text
Attempt incremented

NextAttemptAt calculated

LastError stored
```

---

# 88. Unit — Retry exhaustion

Handler fails maximum permitted attempts.

Verify delivery enters:

```text
Failed / DeadLetter
```

and does not retry endlessly.

---

# 89. Unit — Successful retry

Handler fails first two attempts then succeeds.

Verify:

```text
final state = Processed

error cleared/retained diagnostically as specified

no further retries
```

---

# 90. Unit — Outbox creation

Module transaction publishes Event.

Verify Outbox record contains:

```text
Event ID
Type
Version
Envelope
Payload
Pending state
```

---

# 91. Unit — No direct Event dispatch before transaction commit

Verify transactional publication does not invoke subscribers before owning transaction successfully commits.

---

# 92. Unit — Rollback removes Outbox Event

State mutation transaction rolls back.

Verify Event Outbox insertion also rolls back.

---

# 93. Unit — Event type registration

Register:

```text
forgedeck.build.pipeline-run-started
v1
```

Verify registry resolves serializer/handler contract correctly.

---

# 94. Unit — Duplicate exact Event registration

Register same type/version twice in invalid configuration.

Verify deterministic startup/activation error for that extension registration.

This tests registry correctness, not Event class naming.

---

# 95. Unit — Different versions

Register:

```text
Event X v1

Event X v2
```

Verify both can coexist where supported.

---

# 96. Unit — Module activation registration

Enable Build.

Verify Build's handlers are registered.

---

# 97. Unit — Module disable registration

Disable Build.

Verify Build handlers are absent.

---

# 98. Unit — Review push handler

Given:

```text
GitRepositoryPushEvent
```

for a revision unrelated to any Review Change.

Verify Review performs no Change update.

---

# 99. Unit — Review relevant push

Given Git push affects an active Review Change.

Verify Review updates its domain state and schedules:

```text
ReviewRevisionUpdatedEvent
```

---

# 100. Unit — Build trigger mismatch

Given Git push does not match Build pipeline triggers.

Verify Build does not produce:

```text
BuildPipelineRunRequestedEvent
```

---

# 101. Unit — Build trigger match

Given Git push matches pipeline policy.

Verify Build emits the appropriate run request Event.

---

# 102. Unit — stale Build check

Given Review current revision:

```text
def456
```

and incoming Check Event targets:

```text
abc123
```

verify current revision check status is not incorrectly overwritten.

---

# 103. Unit — Deploy request invalid Release

Given:

```text
DeployReleaseEvent
```

references nonexistent Release.

Verify Deploy does not create Deployment.

Appropriate failure/diagnostic behaviour is produced.

---

# 104. Unit — legal deployment state transition

Given queued Deployment receives Agent-start Event.

Verify:

```text
Queued → Running
```

allowed.

---

# 105. Unit — illegal deployment transition

Given completed Deployment receives stale Start Event.

Verify:

```text
Succeeded → Running
```

is rejected/ignored appropriately.

---

# Integration Testing

## 106. Integration — Outbox database transaction

Use real PostgreSQL.

Perform Module state update that emits Event.

Verify in one committed transaction:

```text
domain record exists

outbox record exists
```

---

# 107. Integration — rollback atomicity

Force failure after domain mutation but before transaction completion.

Verify neither:

```text
domain record
nor
outbox record
```

is committed.

---

# 108. Integration — dispatcher

Insert pending Outbox Event.

Run dispatcher.

Verify:

```text
Event delivered

Outbox status updated
```

---

# 109. Integration — dispatcher restart

Stop dispatcher after Event committed but before successful delivery.

Restart server.

Verify Event is eventually delivered.

---

# 110. Integration — duplicate dispatch

Force same Outbox Event to be delivered twice.

Verify Inbox/idempotency prevents duplicated consumer side effects.

---

# 111. Integration — multiple subscribers

Publish:

```text
GitRepositoryPushEvent
```

with Review and Build installed.

Verify both Module handlers execute independently.

---

# 112. Integration — subscriber failure isolation

Configure:

```text
Review handler works

Build handler throws
```

Publish Git push.

Verify:

```text
Review processing commits

Build delivery enters retry

Git publisher succeeds
```

---

# 113. Integration — no Build Module

Enable Review only.

Publish:

```text
GitRepositoryPushEvent
```

Verify Review reacts.

Verify absence of Build causes no failure/logged exception beyond optional debug diagnostics.

---

# 114. Integration — no Review Module

Enable Build only.

Publish Git push.

Verify Build reacts independently.

---

# 115. Integration — neither consumer installed

Publish Git push with neither Review nor Build enabled.

Verify publication completes successfully.

---

# 116. Integration — enable Module dynamically

Start server with Review disabled.

Publish initial Events.

Enable Review.

Publish a new relevant Event.

Verify Review receives only Events according to configured post-enable semantics and does not automatically replay old Event history.

---

# 117. Integration — disable Module dynamically

Start with Build enabled.

Verify Build consumes Events.

Disable Build.

Publish matching Event.

Verify Build does not handle it.

---

# 118. Integration — re-enable Module

Enable Build again.

Publish new Event.

Verify Build handler resumes.

Verify prior Module data remains intact.

---

# 119. Integration — pending event during disable

Create retrying Build delivery.

Disable Build intentionally.

Verify delivery does not retry indefinitely against a removed subscription.

Delivery should become a deterministic removed/cancelled state according to implementation policy.

---

# 120. Integration — Module update with pending Event

Queue v1 compatible Build Event.

Upgrade Build.

Restart.

Verify updated Build can process supported pending v1 Event.

---

# 121. Integration — unsupported Event version

Deliver Event version unsupported by consumer.

Verify:

```text
consumer does not process invalid payload

failure clearly identifies compatibility/version issue

Core remains healthy
```

---

# 122. Integration — GitHub Connector translation

Send representative GitHub webhook to Connector.

Verify:

```text
signature validated

delivery deduplicated

repository resolved

GitRepositoryPushEvent published
```

No GitHub-specific payload should leak into Review/Build integration handler APIs unless explicitly designed.

---

# 123. Integration — duplicate webhook

Send same GitHub delivery twice.

Verify only one logical source Event or equivalent idempotent outcome.

---

# 124. Integration — invalid webhook signature

Send tampered GitHub webhook.

Verify:

```text
request rejected

no GitRepositoryPushEvent published
```

---

# 125. Integration — manual Pipeline endpoint

Authenticated User with:

```text
build.run
```

calls pipeline run endpoint.

Verify:

```text
HTTP accepted/success response

BuildPipelineRunRequestedEvent produced

Actor identifies User
```

---

# 126. Integration — unauthorized manual Pipeline endpoint

User lacks:

```text
build.run
```

Verify:

```text
403

no BuildPipelineRunRequestedEvent

audit security event generated as designed
```

---

# 127. Integration — Build request workflow

Publish:

```text
BuildPipelineRunRequestedEvent
```

Verify:

```text
PipelineRun persisted

BuildPipelineRunQueuedEvent outboxed
```

---

# 128. Integration — Build queue to Runner dispatch

Process queued Build Run.

Verify expected runner assignment/dispatch state and subsequent:

```text
BuildPipelineRunStartedEvent
```

---

# 129. Integration — Build success

Simulate Runner success.

Verify:

```text
Run status Succeeded

BuildPipelineRunSucceededEvent published

Check Event published where applicable
```

---

# 130. Integration — Build failure

Simulate Runner failure.

Verify:

```text
Run Failed

BuildPipelineRunFailedEvent

appropriate Check status Event
```

---

# 131. Integration — Review Check projection

Publish valid Check Event for active Change revision.

Verify Review-owned projection table updates.

Verify Review does not query Build tables.

---

# 132. Integration — Review projection idempotency

Deliver identical Check Event multiple times.

Verify one projection result.

---

# 133. Integration — Review stale revision

Publish Check for old SHA.

Verify Check remains associated with old revision and cannot unblock latest Change revision.

---

# 134. Integration — Deploy Release event

Publish:

```text
DeployReleaseEvent
```

for valid Release.

Verify:

```text
Deployment persisted

DeployQueuedEvent produced
```

---

# 135. Integration — Deploy success flow

Process:

```text
DeployQueuedEvent
DeployStartedEvent
DeploySucceededEvent
```

Verify Deployment state and current Environment Release change only after success.

---

# 136. Integration — Deploy failure

Produce:

```text
DeployFailedEvent
```

Verify Environment's previously successful Release remains authoritative.

---

# 137. Integration — rollback

Publish rollback request Event.

Verify a new Deployment is created targeting the previous immutable Release.

Do not mutate historical deployment state into a fake rollback.

---

# 138. Integration — correlation propagation

Execute:

```text
Git push
→ Review update
→ Build request
→ Build success
```

Verify every Event shares expected CorrelationId.

Verify each CausationId points to its immediate source.

---

# 139. Integration — two independent flows

Generate two Git pushes simultaneously.

Verify their:

```text
Correlation IDs

Runs

Review projections
```

do not leak into each other.

---

# 140. Integration — cross-project isolation

Publish Event for Project A.

Verify Project B state remains unaffected.

---

# 141. Integration — organisation isolation

Even though one installation currently represents one Organisation, test that Events carrying invalid/mismatched Organisation context cannot mutate resources from a different context if the model later contains test fixtures for multiple scopes.

---

# 142. Integration — licensing failure

Publish Event invoking Enterprise-only behaviour without entitlement.

Verify Module refuses Enterprise behaviour appropriately while Core event infrastructure remains healthy.

---

# 143. Integration — Event diagnostics

Force failed subscriber delivery.

Verify administrative diagnostics can retrieve:

```text
Event ID

Type

Consumer

Attempts

Last Error

Correlation ID
```

without needing to understand payload semantics.

---

# 144. Integration — retry from diagnostics

Retry failed delivery.

Verify same Event ID is retried and does not create a new logical Event unless the user explicitly creates a new workflow.

---

# 145. Integration — service restart with pending retry

Create failed Event scheduled for retry.

Restart ForgeDeck.

Verify retry schedule survives.

---

# 146. Integration — safe mode

Cause optional Module activation failure.

Start ForgeDeck Safe Mode.

Verify:

```text
Core starts

optional handlers inactive

failed Event diagnostics accessible

Module can be disabled
```

---

# E2E Testing

## 147. E2E — Core-only installation

Start ForgeDeck with:

```text
Core only
```

Create:

```text
Organisation

Owner

Project
```

Verify normal Core workflows operate with no optional Module Event handlers registered.

---

# 148. E2E — install GitHub Connector + Code

Start Core-only installation.

Install:

```text
GitHub Connector

Code Module
```

Configure Repository.

Verify Code functionality works.

No Review/Build/Deploy functionality should appear.

---

# 149. E2E — install Review

Install Review.

Verify:

```text
Review navigation appears

Review subscriptions active
```

Create/import Change.

Verify Review state functions without Build installed.

---

# 150. E2E — Git push with Review only

Push commit to repository.

Verify:

```text
GitRepositoryPushEvent occurs

Review reacts

Change revision updates
```

No Build pipeline is created.

No error is shown because Build is absent.

---

# 151. E2E — install Build

Install Build and register Runner.

Verify Build navigation appears.

Verify Build subscriptions become active.

---

# 152. E2E — Git push triggers Review + Build

Push commit.

Expected trace:

```text
GitRepositoryPushEvent
      │
      ├── Review updates revision
      │
      └── Build evaluates trigger
               ↓
BuildPipelineRunRequestedEvent
               ↓
BuildPipelineRunQueuedEvent
               ↓
BuildPipelineRunStartedEvent
               ↓
BuildPipelineRunSucceededEvent
```

Verify UI:

```text
Review displays latest revision

Build displays Run

Review displays resulting Check where integrated
```

---

# 153. E2E — manual Build

From UI select:

```text
Run Pipeline
```

Verify:

```text
BuildPipelineRunRequestedEvent

Actor = logged-in User

Run created

Runner executes

UI streams result
```

---

# 154. E2E — unauthorized Build run

Login as User without `build.run`.

Verify:

```text
Run action absent/disabled according to UX

direct HTTP request rejected

no Run created

no actionable Build request Event produced
```

---

# 155. E2E — failing Build

Push code intentionally causing unit test failure.

Verify:

```text
BuildPipelineRunFailedEvent

Build UI shows Failed

Review Check shows Failed

merge policy blocks merge where configured
```

---

# 156. E2E — superseding revision

Push revision A.

Build A starts.

Push revision B.

Build B starts.

Allow A to finish after B becomes current.

Verify:

```text
A result remains recorded

A does not mark revision B successful

Review latest Check represents B
```

---

# 157. E2E — duplicate Event resilience

During test, force duplicate delivery of Build success Event.

Verify UI/domain state remains singular and correct.

---

# 158. E2E — temporary subscriber failure

Artificially cause Review Check subscriber to fail while Build succeeds.

Verify:

```text
Build remains Succeeded

Review initially missing/stale Check

delivery retries

Review eventually becomes Succeeded
```

No user intervention required for transient failure.

---

# 159. E2E — permanent subscriber failure

Force Review handler to fail beyond retries.

Verify:

```text
Build remains healthy

Review Module remains otherwise accessible where safe

admin Event diagnostics show failed delivery

correlation trace identifies failure
```

---

# 160. E2E — manual failed delivery retry

Fix handler condition.

Admin selects:

```text
Retry
```

Verify projection becomes correct without rerunning the entire Build.

---

# 161. E2E — disable Build

With Review and Build enabled:

```text
disable Build
```

Verify:

```text
Build navigation disappears

Build handlers stop

Review remains functional

existing Build data preserved
```

Push new commit.

Verify Review updates but no Build starts.

---

# 162. E2E — re-enable Build

Re-enable Build.

Push another commit.

Verify Build triggers again.

Verify historical Runs are still visible.

---

# 163. E2E — install Deploy

Install Deploy.

Create Environment/Agent configuration.

Verify Deploy handlers and navigation appear.

---

# 164. E2E — Deploy release

Trigger:

```text
DeployReleaseEvent
```

through UI workflow.

Verify:

```text
Deployment created

Agent executes

DeployStartedEvent

DeploySucceededEvent

Environment shows new Release
```

---

# 165. E2E — failed deployment

Deploy deliberately unhealthy release.

Verify:

```text
DeployFailedEvent

Environment retains previous successful Release

failure visible

Build/Review remain unaffected
```

---

# 166. E2E — rollback

Select previous Release and rollback.

Verify:

```text
rollback request Event

new Deployment record

target previous Release

deployment succeeds

history shows both Deployments
```

---

# 167. E2E — uninstall optional Module

Disable and uninstall Build.

Verify:

```text
Review continues functioning

Deploy continues functioning where independent

Build routes absent

Build handlers absent

no Core startup errors
```

---

# 168. E2E — reinstall Module

Reinstall compatible Build version.

Verify:

```text
historical Build data restored/available according to retention design

new Events handled

other Modules require no reconfiguration
```

---

# 169. E2E — Module update

Update Review while Build remains enabled.

Verify:

```text
pending compatible Events survive

Review reactivates

Build continues operating

cross-module projections remain correct
```

---

# 170. E2E — server restart during workflow

Start Build.

Restart ForgeDeck while:

```text
Pipeline Run active

Outbox contains Events
```

Verify after recovery:

```text
Core starts

pending Event dispatch resumes

Run state reconciles

no duplicate Run created
```

---

# 171. E2E — server restart before dispatch

Commit transaction that creates Run + Outbox Event.

Stop process before dispatcher runs.

Restart.

Verify queued Event is delivered.

---

# 172. E2E — Runner disconnect

Pipeline starts.

Runner disconnects unexpectedly.

Verify Build eventually reaches correct failure/unknown/retry state according to Runner policy and emits corresponding Events.

Other Modules remain operational.

---

# 173. E2E — connector duplicate webhook

Send same external push webhook twice.

Verify:

```text
no duplicate logical Change revision

no unintended duplicate Pipeline Runs

event trace remains explainable
```

---

# 174. E2E — full dogfood workflow

ForgeDeck manages its own repository.

Flow:

```text
Developer pushes ForgeDeck change

        ↓

GitRepositoryPushEvent

        ↓

Review revision updates

        ↓

Build pipeline requested

        ↓

Build executes

        ↓

Checks update Review

        ↓

Reviewer approves

ReviewApprovedEvent

        ↓

Merge

        ↓

Artifact generated

        ↓

Deploy release

        ↓

Separate dogfood ForgeDeck instance updated
```

Verify every Module participates without direct implementation calls between optional Modules.

---

# 175. E2E — Module absence dogfood variants

Run workflow combinations:

```text
Git + Code

Git + Code + Review

Code + Review + Build using GitHub Connector

Code + Build without Review

Review + Build without Deploy

Full Git + Code + Review + Build + Deploy
```

Each valid combination must operate without assumptions that missing Modules exist.

---

# 176. E2E — event trace

Execute complete push-to-build workflow.

Open diagnostic Event trace.

Verify chronological/causal representation can show:

```text
GitRepositoryPushEvent
└── ReviewRevisionUpdatedEvent
    └── BuildPipelineRunRequestedEvent
        └── BuildPipelineRunQueuedEvent
            └── BuildPipelineRunStartedEvent
                └── BuildPipelineRunSucceededEvent
```

---

# 177. E2E — two concurrent workflows

Trigger two independent Builds simultaneously.

Verify:

```text
different correlations

correct Logs

correct Checks

correct Artifacts

correct Project association
```

No cross-contamination.

---

# 178. E2E — failed Module must not kill Core

Install deliberately faulty test Module whose Event subscriber throws during activation or handling.

Verify:

```text
ForgeDeck Core remains available

Organisation/Projects remain available

Module shows Failed/Degraded

other Module Events continue processing
```

---

# 179. E2E — event backlog

Pause dispatcher.

Generate large reasonable test backlog.

Resume dispatcher.

Verify:

```text
all Events eventually processed

ordering assumptions not violated

duplicate effects absent

UI remains responsive
```

Performance thresholds should be set separately from functional correctness.

---

# 180. E2E — unsupported extension version

Install test Module subscribing only to unsupported Event contract.

Verify activation/processing exposes clear compatibility diagnostics instead of corrupting Module data.

---

# 181. E2E — safe mode recovery

Create extension condition that prevents normal extension activation.

Start:

```text
FORGEDECK_SAFE_MODE=true
```

Verify:

```text
Core available

admin can inspect extension state

event diagnostics available

broken Module can be disabled
```

Restart normally and verify healthy Modules resume.

---

# 182. Test project structure

Recommended:

```text
tests/
├── ce/
│   │
│   ├── Messaging.UnitTests/
│   │
│   ├── Messaging.IntegrationTests/
│   │
│   ├── Git.UnitTests/
│   │
│   ├── Review.UnitTests/
│   │
│   ├── Build.UnitTests/
│   │
│   ├── Deploy.UnitTests/
│   │
│   ├── ModuleLifecycle.IntegrationTests/
│   │
│   └── ForgeDeck.E2E/
│
└── ee/
    └── ...
```

Do not create one enormous Events test project containing all Module business tests.

Core messaging tests verify transport/reliability.

Module tests verify semantics.

E2E verifies composition.

---

# 183. Test categories

Useful xUnit categories/traits:

```text
Unit

Integration

E2E

Messaging

ModuleLifecycle

Reliability

Smoke
```

Example execution:

```bash
dotnet test --filter "Category=Unit"
```

and:

```bash
dotnet test --filter "Category=Integration"
```

---

# 184. Event test fixtures

Provide reusable test infrastructure for:

```text
Fake Event Publisher

Event Collector

Event Envelope Factory

Correlation Test Context

Duplicate Delivery Harness

Failure Injection Handler

Outbox Fixture

Inbox Fixture
```

These should help Modules test Events without knowing Core implementation details.

---

# 185. Event collector example

Module unit tests should be able to express:

```csharp
await handler.HandleAsync(pushEvent);

events.ShouldContain<BuildPipelineRunRequestedEvent>();
```

without booting the entire ForgeDeck Server.

---

# 186. Integration environment

Integration tests should use:

```text
real PostgreSQL

real DI configuration

real event serialization

real Outbox

real Inbox

real dispatcher
```

Fake external APIs where the boundary itself is not under test.

---

# 187. E2E environment

E2E should run:

```text
ForgeDeck Server

PostgreSQL

Frontend

required installed Module packages

Runner where Build required

Deployment Agent where Deploy required

fake/local external providers where appropriate
```

using the same extension loading mechanisms as production.

Do not compile all Modules directly into the E2E host merely for convenience if production installs them dynamically.

---

# 188. Completion criteria

This feature is complete when:

```text
Core owns one universal Event mechanism.

There is no separate Command bus.

There is no separate User/System/External Event infrastructure.

Actor metadata identifies origin where useful.

Every Event has stable ID, Type and Version.

Correlation and causation are supported.

Module Events are owned outside Core.

Module Event names conventionally begin with the Module name.

Module Event names conventionally end with Event.

The naming rule is documented but not physically enforced.

Enabled Modules dynamically register Event handlers.

Disabled Modules receive no new Events.

Events with no subscribers are valid.

Multiple subscribers operate independently.

Durable publication uses a transactional Outbox.

Durable consumers use Inbox/idempotency.

Delivery is at-least-once.

Transient failure retries.

Permanent delivery failure is diagnosable.

One broken subscriber does not block unrelated subscribers.

Modules may publish additional Events from handlers.

Accidental loops are prevented by semantic subscription design.

Event contracts are versioned independently from assemblies.

Module updates handle compatible pending Events.

Modules can be installed/disabled/re-enabled without Core knowing Module semantics.

Normal relational Module data remains authoritative.

Provider/query contracts are used for current-state queries.

Unit tests cover Event primitives and Module event semantics.

Integration tests cover persistence, delivery, lifecycle and failure modes.

E2E tests cover real cross-Module workflows and missing-Module combinations.
```

# 189. Final architectural rule

The resulting ForgeDeck programming model should be easy to explain to a new developer:

```text
Need ForgeDeck to react to something?
Publish an Event.

Need to react to something?
Subscribe to the Event.

Need to know current state?
Use the owning Module/provider's query interface.
```

And extension isolation follows naturally:

```text
Core
    does not know Review

Core
    does not know Build

Core
    does not know Deploy

Core
    knows Events.
```

The convention developers follow is similarly straightforward:

> **Every Module Event starts with the Module name and ends with `Event`: `DeployReleaseEvent`, `ReviewRequestedEvent`, `ReviewApprovedEvent`, `BuildPipelineRunStartedEvent`, and so on.**

That convention remains deliberately human-enforced rather than becoming another layer of runtime or compiler machinery.
