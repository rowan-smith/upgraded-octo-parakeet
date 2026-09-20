# Modular Software Delivery Platform

## Licensing

ForgeDeck follows GitLab-style open-core source separation:

- **Community Edition (CE)** — everything outside [`/ee`](ee/): [AGPL-3.0-only](licenses/AGPL-3.0.txt)
- **Enterprise Edition (EE)** — everything under [`/ee`](ee/): [EE License](ee/LICENSE) (proprietary, source-available, sellable)

Default builds are CE. EE: `dotnet run --project src/Platform.Api -p:IncludeEE=true`.  
`FOSS_ONLY=1` forces CE. See [LICENSE.md](LICENSE.md).

Runtime EE capability activation still needs a signed entitlement file; that is separate from copyright licensing.

## Implementation status

The current .NET 10 modular monolith implements Core, native Git, Review, Pipelines, a GitHub source-provider adapter, capability discovery, RBAC checks, correlated audit, versioned domain events, and the dynamic web shell.

Optional modules integrate only through public contracts: Review emits `ChangeOpened`, Git emits `PushReceived`, Pipelines subscribes, and Review discovers results through `ICheckProvider`. No optional module references another optional module.

Run locally with `dotnet run --project src/Platform.Api`, or use `docker compose up --build`. Run `dotnet test ForgeDeck.slnx` for the module composition and workflow tests.

Each feature module follows a consistent `Domain`, `Application`, `Infrastructure`, and `Api` structure. See [the architecture guide](docs/architecture.md) before extending the platform.

---

## 1. Purpose

Build a self-hostable, modular software delivery platform.

The platform provides one unified application, but its functionality is assembled from independently enabled product modules.

Initial product modules:

```text
Core
├── Git
├── Review
├── Pipelines
└── Deploy
```

`Core` is mandatory infrastructure.

`Git`, `Review`, `Pipelines`, and `Deploy` are independently installable and independently licensable capabilities.

An organisation may run:

```text
Core + Git

Core + Review

Core + Pipelines

Core + Deploy

Core + Git + Review

Core + Review + Pipelines

Core + Pipelines + Deploy

Core + Git + Review + Pipelines + Deploy
```

No optional module may require another optional module.

Modules should integrate automatically when compatible modules are present.

The complete platform must feel like one coherent application rather than several separate products combined together.

---

# 2. Product philosophy

The platform follows four principles:

1. **Modular capability**
2. **Progressive complexity**
3. **Provider independence**
4. **One coherent interface**

Customers should install only what they need.

A customer wanting better code review should not need to understand pipelines or deployment.

A customer wanting deployment orchestration should not need to migrate source control.

A customer may gradually expand:

```text
Review
    ↓
Review + Pipelines
    ↓
Review + Pipelines + Deploy
    ↓
Git + Review + Pipelines + Deploy
```

Adding a module enriches the platform rather than replacing the existing workflow.

---

# 3. Terminology

## 3.1 Module

A **Module** is a first-class product capability.

Initial modules:

```text
Git
Review
Pipelines
Deploy
```

Modules may:

* register API routes;
* register frontend routes;
* register navigation;
* own database state;
* publish domain events;
* subscribe to domain events;
* expose provider interfaces;
* provide implementations of shared interfaces;
* contribute UI extensions;
* expose licensed capabilities.

---

## 3.2 Connector

A **Connector** integrates an external system.

Examples:

```text
GitHub
GitLab
Azure DevOps
Bitbucket
Jira
Linear
Slack
Microsoft Teams
HashiCorp Vault
```

Connectors are not product modules.

A connector may implement one or more provider interfaces.

Example:

```text
GitHub Connector
├── ISourceProvider
├── IWebhookProvider
└── IIdentityMappingProvider
```

---

## 3.3 Provider

A **Provider** is an implementation behind a platform contract.

Examples:

```text
ISourceProvider
ICheckProvider
IArtifactProvider
ISecretProvider
ITicketProvider
INotificationProvider
IDeploymentTargetProvider
```

Providers may come from:

* first-party modules;
* first-party connectors;
* third-party extensions.

---

## 3.4 Artifact

An **Artifact** is an immutable output produced by a build or supplied externally.

Artifacts are a domain concept, not initially a separately licensed module.

Examples:

```text
ZIP archive
binary
container image
NuGet package
npm package
deployment bundle
```

---

## 3.5 Release

A **Release** is an immutable set of artifact references and deployment metadata.

Releases belong to the Deploy domain.

Release is not initially a separately licensed module.

---

# 4. Architecture invariants

Every optional module MUST:

1. Depend only on Core and shared contracts.
2. Operate without any other optional module.
3. Own its own business logic.
4. Own its own persistence schema.
5. Communicate with other modules through public contracts or events.
6. Avoid direct references to another module's Application or Infrastructure code.
7. Be removable without breaking unrelated modules.
8. Register its own backend and frontend functionality dynamically.
9. Support independent licensing.
10. Allow an external provider to replace its upstream/downstream integration where practical.

Incorrect:

```text
Review
   ↓
PipelineService
   ↓
DeploymentService
```

Correct:

```text
Review
   │
   └── ChangeUpdated
             │
             ▼
        Event Bus
             │
             ▼
        Pipelines
             │
             └── CheckUpdated
                        │
                        ▼
                   ICheckProvider
                        │
                        ▼
                     Review
```

---

# 5. Platform Core

Core is always installed.

Core provides infrastructure shared by every module.

Core owns:

```text
Organisations
Projects
Users
Teams
Roles
Permissions
Sessions
Authentication
API Tokens
Service Accounts

Module Runtime
Capability Discovery
Licensing
Events
Audit
Notifications
Integration Registry
Provider Registry
Extension Registry
```

Core should remain small.

Business functionality belonging specifically to source control, review, CI, or deployment must not be moved into Core merely because multiple modules use it.

---

# 6. Organisations

An organisation is the top-level tenancy boundary.

```text
Organisation
├── Users
├── Teams
├── Projects
├── Integrations
├── Modules
├── Licensing
└── Settings
```

An organisation may enable different capabilities according to installed modules and licences.

---

# 7. Projects

Projects are the main context users work within.

```text
Organisation
    ↓
Project
```

A project may contain:

```text
Git repositories
Review configuration
Pipeline definitions
Deployment applications
External integrations
```

Only concepts belonging to installed modules should be exposed.

The user should think:

> I am working on Project X.

Not:

> I have switched into another DevOps application.

---

# 8. Identity and authentication

Core must initially support:

```text
local users
password authentication
sessions
API tokens
service accounts
```

The architecture must support future providers:

```text
OIDC
SAML
LDAP
SCIM
```

Authentication belongs to Core.

Identity provider integrations should be connectors/providers rather than hard-coded into feature modules.

---

# 9. RBAC

Core owns the permission engine.

Permissions should be granular and module-oriented.

Examples:

```text
git.repository.read
git.repository.create
git.repository.push
git.repository.manage

review.read
review.comment
review.request
review.approve
review.merge
review.manage

pipelines.read
pipelines.run
pipelines.cancel
pipelines.manage
pipelines.runner.manage

deploy.read
deploy.execute
deploy.approve
deploy.manage
```

Roles map to permission sets.

Modules register their permissions when loaded.

Disabled modules should not register their permissions.

---

# 10. Audit

Core provides a shared append-only audit stream.

Every audit record should support:

```text
timestamp
actor
organisation
project
module
action
resource
resource ID
metadata
correlation ID
```

Examples:

```text
git.repository.created
git.branch.deleted

review.change.approved
review.change.merged

pipeline.run.started
pipeline.run.completed

deploy.release.created
deploy.deployment.started
deploy.deployment.approved
deploy.deployment.completed
```

Modules publish audit events through a shared Core abstraction.

---

# 11. Module runtime

Core must expose an explicit module contract.

Conceptually:

```csharp
public interface IPlatformModule
{
    string Id { get; }

    void RegisterServices(...);

    void RegisterRoutes(...);

    void RegisterPermissions(...);

    void RegisterCapabilities(...);

    void RegisterEventHandlers(...);

    void RegisterExtensions(...);
}
```

Example manifest:

```json
{
  "id": "review",
  "name": "Review",
  "version": "0.1.0",
  "edition": "community",
  "capabilities": [
    "Review.BasicApproval"
  ]
}
```

Core exposes module state:

```http
GET /api/platform/modules
```

Example:

```json
{
  "modules": [
    {
      "id": "git",
      "enabled": false
    },
    {
      "id": "review",
      "enabled": true,
      "edition": "commercial"
    },
    {
      "id": "pipelines",
      "enabled": true,
      "edition": "community"
    },
    {
      "id": "deploy",
      "enabled": false
    }
  ]
}
```

Frontend composition must be derived from this state.

---

# 12. Licensing

Licensing occurs per module and per capability.

There is no platform-wide:

```text
Free
Premium
Enterprise
Ultimate
```

feature ladder.

An organisation may have:

```text
Git          Community
Review       Commercial
Pipelines    Community
Deploy       Commercial
```

Capability checks should look like:

```csharp
capabilities.Has(
    organisationId,
    "Review.MultiApproval");
```

Avoid:

```csharp
license.Tier == Enterprise
```

Commercial functionality should primarily target:

```text
governance
scale
advanced policy
automation
compliance
organisation management
enterprise integrations
```

Community editions should provide genuinely usable workflows.

---

# 13. Git module

Git provides native Git repository hosting.

It is a first-class optional module.

Git must operate without Review, Pipelines, or Deploy.

## 13.1 Git responsibilities

Git owns:

```text
Repositories
Branches
Commits
Tags
Refs

Clone
Fetch
Push

SSH Git access
HTTPS Git access

Repository permissions
Repository settings

Webhooks
Repository events

Basic repository browser
Commit browser
Branch browser
Tag browser
```

Future functionality may include:

```text
LFS
repository mirroring
protected branches
signed commit enforcement
advanced repository policies
repository archival
```

---

# 14. Source provider abstraction

Consumers of source control must use:

```csharp
ISourceProvider
```

Conceptually:

```csharp
public interface ISourceProvider
{
    Task<Repository> GetRepository(...);

    Task<IReadOnlyList<Branch>> GetBranches(...);

    Task<IReadOnlyList<Commit>> GetCommits(...);

    Task<Diff> GetDiff(...);

    Task<MergeResult> Merge(...);
}
```

Possible implementations:

```text
Git Module
GitHub Connector
GitLab Connector
Azure Repos Connector
Bitbucket Connector
```

Review must not know whether source code lives inside the platform.

---

# 15. Git events

Git may publish:

```text
RepositoryCreated
RepositoryDeleted

BranchCreated
BranchUpdated
BranchDeleted

CommitReceived

TagCreated

PushReceived
```

Other modules may subscribe.

Example:

```text
Git Push
   ↓
PushReceived
   ↓
Pipelines
   ↓
Pipeline Run
```

Git does not invoke Pipelines directly.

---

# 16. Review module

Review provides standalone code review.

Review must operate without the Git module.

Review consumes:

```text
ISourceProvider
```

The provider may be:

```text
native Git module
GitHub
GitLab
Azure Repos
Bitbucket
```

---

# 17. Change domain

The internal domain concept should be:

```text
Change
```

rather than GitHub-specific terminology such as Pull Request.

A Change contains:

```text
title
description

author

source repository
source branch

target repository
target branch

commits
diff

reviewers
reviews
comments

approval state
merge state

external reference
linked work items
```

External mappings:

```text
GitHub       Pull Request
GitLab       Merge Request
Azure Repos  Pull Request
Platform     Change
```

---

# 18. Change workflow

Initial states:

```text
Draft
Open
Changes Requested
Approved
Mergeable
Merged
Closed
```

State transitions should be event-driven.

Review may publish:

```text
ChangeCreated
ChangeUpdated
ReviewRequested
ChangeApproved
ChangesRequested
ChangeMerged
ChangeClosed
```

---

# 19. Review interface

Initial Change UI:

```text
Change #184

Title
Author
Source → Target
Status

Overview
Changes
Discussion
Reviewers
```

Module extensions may enrich this page.

With Pipelines:

```text
Overview
Changes
Discussion
Reviewers
Checks
```

With Deploy:

```text
Overview
Changes
Discussion
Reviewers
Checks
Deployments
```

The Review frontend must not import Pipelines or Deploy implementations directly.

These additions should use registered UI extension points.

---

# 20. Review comments

Support:

```text
general discussion
inline diff comments
threads
replies
resolve/reopen
historical diff context
```

Comments should remain understandable after new commits modify affected lines.

---

# 21. Review approval policy

Community functionality should initially include:

```text
request reviewers
approve
request changes
require one approval
block merge on active changes-requested review
```

Multiple people may still review and comment.

The Community restriction is on advanced enforceable policy rather than preventing collaboration.

Possible Commercial capabilities:

```text
Review.MultiApproval
Review.TeamApproval
Review.CodeOwners
Review.PathPolicy
Review.ConditionalPolicy
Review.PolicyComposition
Review.ReviewDismissal
```

Examples:

```text
Require 2 approvals

Require Backend Team approval

If /security/** changes:
    require Security Team

If /infrastructure/** changes:
    require DevOps Team

Require all discussions resolved
```

---

# 22. Pipelines module

Pipelines provides CI and general workflow automation.

Pipelines must operate without Git or Review.

Valid triggers may include:

```text
manual
schedule
webhook

external Git push
native Git push

change opened
change updated
change merged

tag created
```

Unavailable trigger types simply do not exist when their provider is absent.

---

# 23. Pipeline domain

Core concepts:

```text
Pipeline
Pipeline Definition
Pipeline Run

Stage
Job
Step

Trigger

Runner
Runner Pool

Check

Artifact Reference

Log
Test Result
```

---

# 24. Pipeline execution

MVP pipeline execution should support:

```text
pipeline definition
manual execution
Git push trigger

sequential jobs
sequential steps

environment variables

process/container execution

streaming logs
exit codes

cancellation

basic output/artifact publishing
```

Execution must occur outside the central control-plane process.

```text
Platform Control Plane
        │
        ▼
      Runner
        │
        ▼
 Container / Process
```

---

# 25. Runners

Runners execute pipeline workloads.

Initial support should target self-hosted runners.

A runner should:

```text
register
authenticate
advertise capabilities
receive jobs
execute jobs
stream logs
report results
handle cancellation
```

Future capabilities:

```text
runner pools
labels
autoscaling
ephemeral runners
Kubernetes runners
cloud runners
isolated execution
```

---

# 26. Checks

Pipelines should expose successful/failed work as:

```text
Check
```

through:

```csharp
ICheckProvider
```

Examples:

```text
Build
Unit Tests
Integration Tests
Lint
Security Scan
```

Review can display checks without knowing how they were produced.

External systems may also implement `ICheckProvider`.

---

# 27. Pipeline artifacts

Pipelines may produce immutable outputs.

Artifact references should be provider-neutral.

Conceptually:

```text
ArtifactReference

ID
Name
Version
URI
Digest
Type
Metadata
Provenance
```

Examples:

```text
internal://artifacts/123

ghcr.io/acme/api@sha256:...

nuget://Acme.Core/2.8.4

https://storage.example/build-184.zip
```

Pipelines must not require built-in artifact storage.

---

# 28. Artifact provider abstraction

Define:

```csharp
IArtifactProvider
```

Possible implementations:

```text
built-in simple artifact storage
OCI registry
GitHub Container Registry
Azure Container Registry
Harbor
NuGet feed
generic HTTP storage
```

Artifact storage is infrastructure behind Pipelines and Deploy, not initially a separately licensed product module.

A dedicated Artifacts module may be introduced later if product requirements justify it.

---

# 29. Deploy module

Deploy provides release and deployment orchestration.

Deploy must operate without Git, Review, or Pipelines.

A valid installation may therefore be:

```text
GitHub
   ↓
GitHub Actions
   ↓
GHCR
   ↓
Deploy Module
```

Deploy consumes artifact references from external or internal sources.

---

# 30. Deploy domain

Deploy owns:

```text
Applications

Releases

Environments

Deployment Processes

Deployment Targets

Deployment Agents

Deployments

Approvals

Promotion

Rollback

Deployment History
```

---

# 31. Applications

An Application is something deployable.

Examples:

```text
API
Frontend
Worker
Complete product
Industrial system
Customer installation
```

A project may contain multiple Applications.

---

# 32. Releases

A Release is an immutable deployment candidate.

Example:

```text
Release 2.8.4

Components
├── frontend
│   └── ghcr.io/acme/frontend@sha256:abc
│
├── api
│   └── ghcr.io/acme/api@sha256:def
│
└── simulator
    └── simulator-1.8.1.zip
```

Release creation must support:

```text
Pipeline output
Internal artifact
External artifact
Manual artifact reference
```

Deploy does not require Pipelines.

---

# 33. Environments

Deploy manages named environments.

Examples:

```text
Development
Integration
Test
Staging
Production
```

Environment topology is configurable.

Do not hard-code a fixed sequence.

---

# 34. Promotion

The intended deployment model is:

```text
Build once
   ↓
Release
   ↓
Development
   ↓
Test
   ↓
Staging
   ↓
Production
```

The same immutable artifact should move through environments.

The platform should discourage:

```text
rebuilding production separately
```

unless explicitly configured.

---

# 35. Deployment targets

A target is a location capable of receiving a deployment.

Possible targets:

```text
Docker host
Kubernetes cluster
Windows host
Linux host
filesystem
SSH target
cloud service
industrial edge machine
custom provider
```

Targets are accessed through provider abstractions.

---

# 36. Deployment agents

Support remote agents.

Agents should preferably establish outbound authenticated connections to the control plane.

```text
Central Platform
      ▲
      │ secure outbound connection
      │
Deployment Agent
      │
      ├── Docker
      ├── Kubernetes
      ├── Linux
      ├── Windows
      └── custom target
```

This supports:

```text
on-premise environments
customer sites
industrial networks
restricted networks
hybrid systems
edge deployment
```

without exposing inbound management ports.

---

# 37. Deployment approvals

Community may initially support:

```text
manual deployment
basic environment deployment
basic deployment history
```

Potential Commercial capabilities:

```text
Deploy.Approvals
Deploy.MultiApproval
Deploy.EnvironmentPolicy
Deploy.PromotionPolicy
Deploy.MultiSite
Deploy.AdvancedRollback
Deploy.ChangeWindow
Deploy.ComplianceAudit
```

Example:

```text
Production

Require:
    2 approvals

Release must:
    have passed Staging

Deployment must:
    occur during approved change window
```

---

# 38. Shared policy architecture

Review and Deploy both need policy evaluation.

Design toward a shared Core policy contract without forcing both modules to use identical domain models.

Conceptually:

```text
Policy
Condition
Requirement
Evaluation
Result
Override
```

Examples:

```text
Review:
Require Backend Team approval

Deploy:
Require Production approval
```

Do not over-engineer a generic rule engine in MVP.

Build the abstraction only as common requirements emerge.

---

# 39. Connectors

Initial connector targets:

```text
GitHub
```

Then:

```text
GitLab
Azure DevOps
Bitbucket
Jira
```

Potential future connectors:

```text
Linear
YouTrack
Slack
Microsoft Teams
Vault
AWS
Azure
GCP
Harbor
SonarQube
Sentry
```

Connectors may provide multiple capabilities.

---

# 40. Ticket integration

Do not build ticketing into the initial product.

Ticket providers may expose:

```csharp
ITicketProvider
```

Example systems:

```text
Jira
Linear
Azure Boards
YouTrack
GitHub Issues
```

Changes may reference work items:

```text
AXO-184
```

Relevant lifecycle information may be pushed to ticket providers:

```text
branch created
change opened
change merged
pipeline passed
release created
deployment completed
```

---

# 41. Event architecture

Modules communicate using stable domain events.

Examples:

```text
RepositoryCreated
PushReceived

ChangeCreated
ChangeUpdated
ChangeApproved
ChangeMerged

PipelineRunStarted
PipelineRunCompleted
CheckUpdated
ArtifactProduced

ReleaseCreated
DeploymentStarted
DeploymentCompleted
DeploymentFailed
```

Events should contain:

```text
stable IDs
event version
timestamp
correlation ID
minimal durable payload
```

Do not pass module implementation objects through events.

---

# 42. Frontend extension architecture

The application uses a shared shell.

```text
Application Shell
├── Authentication
├── Organisation Context
├── Project Context
├── Navigation
├── Notifications
├── Command Palette
└── Extension Registry
```

Modules may register:

```text
navigation entries
routes
dashboard cards
tabs
actions
settings pages
commands
resource panels
```

Example:

```text
Review.ChangePage
├── Overview
├── Changes
├── Discussion
├── Reviewers
│
├── [Pipelines: Checks]
└── [Deploy: Deployments]
```

Extensions should disappear automatically when the contributing module is absent.

---

# 43. Navigation

Review only:

```text
Overview

Review
├── Changes
└── Review Queue

Settings
```

Git + Review:

```text
Overview

Code
├── Repositories
├── Branches
└── Tags

Review
├── Changes
└── Review Queue

Settings
```

Review + Pipelines:

```text
Overview

Review
├── Changes
└── Review Queue

Pipelines
├── Definitions
├── Runs
└── Runners

Settings
```

Complete installation:

```text
Overview

Code
├── Repositories
├── Branches
└── Tags

Review
├── Changes
└── Review Queue

Pipelines
├── Pipelines
├── Runs
└── Runners

Deploy
├── Releases
├── Environments
└── Deployments

Settings
```

Disabled modules should not appear as permanently greyed-out navigation.

---

# 44. Backend architecture

Start as a modular monolith.

Do not build one microservice per module.

Suggested structure:

```text
src/

Platform.Core/
Platform.Contracts/
Platform.Api/
Platform.Web/

Modules/

  Git/
    Git.Domain/
    Git.Application/
    Git.Infrastructure/

  Review/
    Review.Domain/
    Review.Application/
    Review.Infrastructure/

  Pipelines/
    Pipelines.Domain/
    Pipelines.Application/
    Pipelines.Infrastructure/

  Deploy/
    Deploy.Domain/
    Deploy.Application/
    Deploy.Infrastructure/

Connectors/

  GitHub/
  GitLab/
  AzureDevOps/
  Jira/

Workers/

  PipelineRunner/
  DeploymentAgent/
```

Modules must not directly reference another module's:

```text
Application
Infrastructure
Persistence
```

projects.

---

# 45. Persistence

A shared relational database may be used initially.

Each module owns its logical schema.

Example:

```text
core.*

git.*

review.*

pipelines.*

deploy.*
```

Each module owns its migrations.

A module must not directly query another module's tables.

Cross-module association uses stable IDs.

---

# 46. APIs

Expose APIs for meaningful operations.

Suggested grouping:

```text
/api/core/*

/api/git/*

/api/review/*

/api/pipelines/*

/api/deploy/*
```

Disabled modules must not register their routes.

The frontend should use the same application APIs wherever practical.

---

# 47. Security

All security enforcement occurs server-side.

Frontend visibility is not authorisation.

Every operation must evaluate:

```text
authentication
organisation membership
project access
module availability
permission
licensed capability
resource ownership/access
```

Secrets must not be stored unencrypted.

API tokens should support scopes and expiry.

Sensitive actions must be audited.

---

# 48. Self-hosting

Self-hosting is a first-class requirement.

A minimal installation should require approximately:

```text
Platform Server
Database
```

Optional components:

```text
Pipeline Runner
Deployment Agent
External object storage
External identity provider
External secret provider
```

Initial developer deployment target:

```bash
docker compose up
```

Avoid requiring Kubernetes or a large distributed control plane merely to run the application.

---

# 49. Community and commercial architecture

Community modules should be open and useful.

Commercial implementations should be separable from Community code where practical.

Example:

```text
Review Community
└── SingleApprovalPolicy
```

Commercial extension:

```text
Review Commercial
├── MinimumApprovalsPolicy
├── TeamApprovalPolicy
├── CodeOwnerPolicy
└── ConditionalApprovalPolicy
```

Similar boundaries may eventually exist for:

```text
Git
Pipelines
Deploy
```

Licensing should activate capabilities rather than introduce a global product tier.

---

# 50. MVP scope

The first milestone is still:

```text
Core
+
Review
+
GitHub Connector
```

Do not implement native Git hosting first.

The purpose of the initial milestone is proving:

```text
module loading
provider contracts
dynamic UI composition
capability enforcement
event architecture
```

MVP workflow:

```text
Create organisation

Create project

Connect GitHub repository

Import/open Change

Display diff

Comment

Request reviewer

Approve

Enforce basic approval

Merge

Audit actions
```

---

# 51. MVP extensibility proof

Before implementing Pipelines, create a trivial extension module.

Example:

```text
Checks Demo
```

Without extension:

```text
Change
├── Overview
├── Changes
├── Discussion
└── Reviewers
```

With extension:

```text
Change
├── Overview
├── Changes
├── Discussion
├── Reviewers
└── Checks
```

This verifies that another module can enrich Review without modifying Review itself.

---

# 52. Development roadmap

## Phase 1 — Platform foundation

Implement:

```text
Core
Review
GitHub Connector

Module Runtime
Provider Registry
Event System
Capabilities
RBAC
Audit
UI Extension Framework
```

Prove:

```text
Review ON
    → routes/navigation/UI exist

Review OFF
    → routes/navigation/UI do not exist
```

---

## Phase 2 — Git

Implement native repository hosting.

Required proof:

```text
Review + GitHub
```

and:

```text
Review + Native Git
```

must both function through the same `ISourceProvider` contract.

Review must not require modification to switch providers.

---

## Phase 3 — Pipelines

Implement:

```text
pipeline definitions
runner
manual execution
Git push execution
logs
checks
basic artifact outputs
```

Required proof:

```text
Git Push
   ↓
Pipeline
```

and:

```text
Change Updated
   ↓
Pipeline
   ↓
Check
   ↓
Review UI
```

Review should consume `ICheckProvider` rather than Pipelines directly.

---

## Phase 4 — Deploy

Implement:

```text
Applications
Releases
Environments
Deployment Targets
Agents
Deployments
Basic Promotion
Basic Rollback
```

Required proof:

```text
Pipeline Artifact
      ↓
Deploy
```

and independently:

```text
External Artifact
      ↓
Deploy
```

Deploy must not require Pipelines.

---

# 53. Non-goals

Do not initially implement:

```text
ticketing
sprint planning
roadmaps
wiki

IDE
AI coding assistant

observability platform
feature flags

Terraform replacement

full Kubernetes management

advanced security suite

all package registry protocols

full enterprise identity suite
```

These may become connectors, providers, or future modules.

---

# 54. Architectural success criteria

The following configurations must eventually work:

```text
Core + Git

Core + Review

Core + Pipelines

Core + Deploy

Core + Git + Review

Core + Review + Pipelines

Core + Pipelines + Deploy

Core + Git + Review + Pipelines + Deploy
```

Additionally:

```text
Review + GitHub
Review + GitLab
Review + Native Git
```

should require no provider-specific Review business logic.

Likewise:

```text
Deploy + internal artifact
Deploy + GHCR
Deploy + ACR
Deploy + generic URL artifact
```

should operate through provider contracts.

---

# 55. Definition of modularity

A module is genuinely modular only when:

1. It may be absent entirely.
2. Other optional modules continue working when it is absent.
3. Its API routes disappear when absent.
4. Its frontend routes disappear when absent.
5. Its navigation disappears when absent.
6. Its background workers disappear when absent.
7. Its persistence is independently owned.
8. It exposes stable contracts/events.
9. Other modules do not query its database.
10. Other modules do not reference its implementation assemblies.
11. It may be independently licensed.
12. Compatible third-party implementations may replace provider-facing functionality.

---

# 56. Long-term platform model

The complete first-party path is:

```text
Git
 │
 ▼
Review
 │
 ▼
Pipelines
 │
 ▼
Deploy
```

These arrows mean:

> integrates with

They do **not** mean:

> requires

Each stage may instead be external:

```text
GitHub
   │
Platform Review
   │
Jenkins
   │
Platform Deploy
```

or:

```text
Platform Git
   │
Platform Review
   │
GitHub Actions
   │
Octopus Deploy
```

or:

```text
GitLab
   │
Platform Review
   │
Platform Pipelines
   │
Platform Deploy
```

The platform's defining characteristic is not ownership of every step.

It is providing **independently adoptable software-delivery capabilities that become one coherent system when combined**.

---

# 57. Initial instruction to implementation agents

When implementing features:

1. Do not introduce dependencies between optional modules.
2. Prefer provider contracts over module-specific APIs.
3. Prefer domain events over direct cross-module invocation.
4. Keep Core free of module business logic.
5. Make UI composition dynamic.
6. Make capability checks server-side.
7. Keep Community and commercial capability boundaries explicit.
8. Do not introduce distributed infrastructure without a demonstrated requirement.
9. Do not implement future modules prematurely.
10. Optimise first for architectural composability, then feature breadth.

The first architectural milestone remains:

> Prove that one coherent application can dynamically compose independently enabled modules without those modules becoming implementation-dependent on one another.
