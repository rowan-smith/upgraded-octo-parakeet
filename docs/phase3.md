# Phase 3 — Working Local Deployment

## 1. Objective

Extend the existing Review + Pipelines platform with a working Deploy module capable of taking a validated pipeline output and deploying it to a Docker environment on the developer's local machine.

The developer workflow becomes:

```text
Local Development
      |
      | git push
      v
    GitHub
      |
      v
    Review
      |
      v
  Pipelines
      |
      +-- Build
      +-- Unit
      +-- Integration
      +-- E2E
      |
      v
 Deployable Output
      |
      v
    Release
      |
      v
    Deploy
      |
      v
Local Docker Environment
```

Phase 3 should provide the first complete software-delivery path:

```text
Change
  ↓
Validation
  ↓
Release
  ↓
Deployment
```

The target environment for this phase is intentionally local.

Do not attempt production-grade cloud or Kubernetes deployment yet.

---

# 2. Phase 3 success condition

Phase 3 is complete when the following workflow works end-to-end:

```text
1. Develop a Change.

2. Push the branch.

3. Review the Change.

4. Run the validation pipeline.

5. Unit, Integration, and E2E checks pass.

6. Pipeline produces a deployable artifact.

7. Merge the Change.

8. Create a Release from the validated artifact.

9. Deploy the Release to the local Development environment.

10. Deployment executes through a registered local Deployment Agent.

11. Docker containers are created/updated.

12. The platform verifies deployment health.

13. The environment displays the deployed Release.

14. Deployment history is retained.

15. Deploy a newer Release.

16. Roll back to the previous Release.

17. Environment returns to the previous known-good version.
```

The platform should be capable of deploying its own application locally.

---

# 3. Scope

Implement:

```text
Deploy Module

Applications

Releases

Release Components

Artifact References

Environments

Deployment Targets

Deployment Agents

Deployment Processes

Deployment Steps

Deployments

Deployment Execution

Docker Deployment Provider

Health Verification

Deployment Logs

Deployment History

Rollback

Review / Pipeline integration

Audit
```

Do not implement:

```text
Production cloud deployment

Kubernetes deployment

AWS deployment

Azure deployment

GCP deployment

Remote SSH deployment

Advanced deployment strategies

Blue/green deployment

Canary deployment

Traffic shifting

Multi-region deployment

Complex change windows

Enterprise approval chains

Infrastructure provisioning

Terraform execution

Secrets-management platform

Container registry product

Environment configuration product
```

---

# 4. Architectural invariants

Deploy is an independent optional module.

Deploy MUST NOT depend directly on:

```text
Review
Pipelines
GitHub
```

Deploy consumes provider-neutral concepts such as:

```text
ArtifactReference
Release
DeploymentTarget
```

Integration should occur through:

```text
events
provider contracts
extension points
stable IDs
```

Correct:

```text
Pipelines
    |
    | ArtifactProduced
    v
Event Bus
    |
    v
Deploy
```

Incorrect:

```text
DeployService
    -> PipelineService
```

Likewise Review may display deployment information through an extension contract, but must not reference Deploy implementation types.

---

# 5. Phase 3 module responsibilities

Deploy owns:

```text
Applications

Releases

Release Components

Environments

Deployment Targets

Deployment Agents

Deployment Processes

Deployment Steps

Deployments

Deployment Execution State

Deployment Logs

Health Verification

Rollback

Deployment History
```

Pipelines owns:

```text
Pipeline Runs

Build Outputs

Run Artifacts

Artifact publication
```

Core owns:

```text
Projects

Users

RBAC

Capabilities

Events

Audit

Module Runtime
```

---

# 6. Application domain

An Application represents something deployable.

Examples:

```text
Platform API

Platform Web

Complete Platform

Background Worker

Customer Application
```

For Phase 3, support one or more Applications per Project.

Example:

```text
Project
Platform

Applications

Platform
```

or:

```text
Applications

Platform API
Platform Web
```

The implementation should support multiple Applications, even if dogfooding initially deploys a single combined application.

---

# 7. Application model

Conceptually:

```text
Application

Id
ProjectId

Name
Description

DefaultDeploymentProcessId

CreatedAt
UpdatedAt
```

An Application has:

```text
Releases
Deployment Process
Environment Deployments
```

---

# 8. ArtifactReference

Phase 2 introduced deployable pipeline outputs.

Phase 3 formalises:

```text
ArtifactReference
```

Conceptual fields:

```text
Id

Provider

Type

Name

Version

Uri

Digest

Metadata

CreatedAt

SourceCommitSha

SourcePipelineRunId
```

Possible types:

```text
OCI Image

Docker Compose Bundle

ZIP

Tar Archive

Generic File

External Reference
```

For Phase 3, prioritise:

```text
Docker Image

Docker Compose Bundle
```

---

# 9. Artifact immutability

A Release MUST refer to immutable artifact identity.

Preferred:

```text
repository/image@sha256:abcdef...
```

rather than:

```text
repository/image:latest
```

Tags may be displayed as metadata.

Do not use mutable tags as the authoritative Release identity.

Example:

```text
Image

platform-api:0.3.4

Digest

sha256:c82...
```

---

# 10. Pipeline output

A successful pipeline may publish deployable output.

Example pipeline:

```text
Validate & Package

Build
   ↓
Unit
   ↓
Integration
   ↓
E2E
   ↓
Docker Build
   ↓
Publish Artifact
```

Output:

```text
ArtifactReference

Name
platform

Version
0.3.4+abc123

Type
OCI

Digest
sha256:...
```

For Phase 3, publishing to the developer's local Docker engine or a local registry is acceptable.

---

# 11. Pipeline / Deploy boundary

Pipelines MAY emit:

```text
ArtifactProduced
```

Deploy MAY subscribe.

However, producing an artifact MUST NOT automatically mean deployment.

Conceptually:

```text
Pipeline
   ↓
Artifact
   ↓
Available for Release
```

Then:

```text
User
   ↓
Create Release
```

or later:

```text
Release automation policy
```

Automatic production/CD is explicitly deferred.

---

# 12. Releases

A Release is an immutable deployment candidate.

A Release contains:

```text
Release ID

Application

Version

Artifact References

Source Metadata

CreatedAt

CreatedBy
```

Example:

```text
Release 0.3.4

Application
Platform

Source
Commit abc123

Pipeline
#184

Components

platform-api
    sha256:123...

platform-web
    sha256:456...
```

Once created, artifact references in a Release MUST NOT change.

---

# 13. Release creation

Allow:

```text
[ Create Release ]
```

Sources:

```text
Pipeline Artifact

Existing Artifact Reference

External Artifact
```

Initial dogfood path:

```text
Successful Pipeline
        |
        v
[ Create Release ]
```

Suggested defaults:

```text
Version
0.3.4

Artifacts
automatically populated from pipeline output

Source Commit
abc123

Source Pipeline
#184
```

---

# 14. Release versioning

Do not hard-code semantic versioning as a requirement.

Support arbitrary release identifiers.

Examples:

```text
0.3.4

2026.09.20.1

abc123

release-184
```

Provide recommended SemVer-style behaviour where convenient.

The Release ID and artifact digest remain authoritative.

---

# 15. Release page

Example:

```text
Release 0.3.4

Application
Platform

Status
Available

Created
20 Sep 2026 14:32

Source
Commit abc123

Pipeline
Validate #184

Artifacts

platform
sha256:8fa...

Deployments

Development
Not Deployed
```

Actions:

```text
Deploy

View Pipeline

View Commit
```

---

# 16. Environments

Deploy owns Environments.

Phase 3 initially requires:

```text
Development
```

Architecture must support later:

```text
Development

Integration

Test

Staging

Production
```

Do not hard-code a fixed environment list.

---

# 17. Environment model

Conceptually:

```text
Environment

Id
ProjectId

Name
Description

Order

ProtectionLevel

CreatedAt
```

Phase 3 may use:

```text
Development
```

with no approval requirement.

---

# 18. Environment page

Example:

```text
Environment

Development

Status
Healthy

Current Release
0.3.4

Deployed
2 minutes ago

Target
Rowan-PC Docker

Applications

Platform
0.3.4
Healthy
```

Display:

```text
current release

deployment status

target

health

deployment history
```

---

# 19. Deployment targets

A Deployment Target is something capable of receiving a deployment.

Phase 3 target:

```text
Local Docker
```

Future targets:

```text
Remote Docker

Kubernetes

Linux Host

Windows Host

SSH Host

Cloud Service

Industrial Edge Host
```

Do not implement future providers yet.

---

# 20. Target abstraction

Define something conceptually equivalent to:

```csharp
public interface IDeploymentTargetProvider
{
    Task<DeploymentTargetStatus> GetStatusAsync(...);

    Task ExecuteStepAsync(...);

    Task<HealthResult> VerifyAsync(...);
}
```

The exact abstraction may instead separate:

```text
DeploymentAgent

TargetProvider

StepExecutor
```

The key requirement is that Deploy business logic does not directly call Docker APIs.

---

# 21. Deployment agent

Deployment execution must occur outside the control-plane application.

Architecture:

```text
Platform Control Plane
        |
        | secure agent protocol
        v
 Deployment Agent
        |
        v
 Deployment Target
        |
        v
      Docker
```

This mirrors the Pipelines runner architecture.

The Deploy module controls intent.

The Deployment Agent performs the actual target-side operations.

---

# 22. Local deployment agent

Phase 3 requires one local agent.

Example:

```text
Deployment Agent

Name
Rowan-PC

Status
Online

Capabilities
docker
docker-compose

Targets

Local Docker
```

The agent may run as:

```text
separate .NET worker
```

and may potentially share infrastructure with the Pipeline Runner later.

Do not prematurely combine them into one process if it harms module isolation.

---

# 23. Agent connection direction

Deployment agents should initiate outbound connections:

```text
Agent
  |
  | outbound authenticated connection
  v
Platform
```

Do not require the Platform to open inbound connections to agents.

This prepares for future:

```text
customer sites

industrial networks

edge installations

restricted environments
```

---

# 24. Deployment agent registration

Provide:

```text
Deploy
    Agents

[ Add Agent ]
```

Generate:

```text
Registration Token
```

Agent supplies:

```text
Platform URL

Registration Token

Agent Name
```

After registration:

```text
temporary token
      ↓
durable agent credential
```

Registration tokens should:

```text
expire

be revocable

preferably be single-use
```

---

# 25. Agent heartbeat

Agent reports:

```text
Online

Busy

Offline

Capabilities

Version

Current deployments

Last heartbeat
```

If heartbeat expires:

```text
Agent = Offline
```

Deployment should not begin when its required agent is unavailable.

---

# 26. Deployment targets UI

Example:

```text
Deploy
    Targets

Local Docker

Agent
Rowan-PC

Provider
Docker

Status
Available
```

Configuration may include:

```text
Docker endpoint

Project/network naming prefix

Base working directory
```

Do not expose raw credentials after configuration.

---

# 27. Docker deployment provider

Implement:

```text
DockerDeploymentProvider
```

Capabilities:

```text
pull/load image

create/update container

stop container

remove replaced container

configure environment variables

configure volumes

configure networks

configure ports

inspect container

check health
```

If using Docker Compose for the initial dogfood deployment, provide a Compose-backed implementation while preserving a provider-neutral Deploy domain.

---

# 28. Docker Compose support

Phase 3 may deploy through a generated or supplied:

```text
docker-compose.yml
```

or equivalent Compose model.

Example:

```text
Release
   ↓
Deployment
   ↓
Compose deployment bundle
   ↓
docker compose up -d
```

Avoid tightly coupling all Deploy concepts to Compose.

Compose is one deployment execution mechanism.

---

# 29. Deployment process

An Application has a Deployment Process.

A process consists of ordered Steps.

Example:

```text
Deploy Platform

1. Prepare Release

2. Pull Images

3. Apply Compose Configuration

4. Start Containers

5. Wait for Health

6. Verify Application
```

---

# 30. Deployment step model

Conceptually:

```text
DeploymentStep

Id

Name

Order

Type

Configuration

Timeout

Required
```

Initial step types:

```text
Docker Pull

Docker Compose Up

Wait for Container Health

HTTP Health Check
```

Optional:

```text
Run Command
```

if necessary for dogfooding.

Do not create a universal workflow language yet.

---

# 31. Deployment variables

Deployment should support variables.

Scopes:

```text
Application

Environment

Target

Release

Deployment
```

Example:

```text
ASPNETCORE_ENVIRONMENT=Development

PORT=8080
```

Resolution should be deterministic.

Sensitive values should be represented separately as secrets.

---

# 32. Secrets

Support a minimal secret mechanism if deployment requires secrets.

Secrets:

```text
encrypted at rest

redacted in UI

redacted from logs where practical

injected only when executing
```

Do not build a full secrets platform.

Preserve future:

```text
ISecretProvider
```

for:

```text
Vault

Azure Key Vault

AWS Secrets Manager
```

---

# 33. Deployment creation

From a Release page:

```text
[ Deploy ]
```

Form:

```text
Release
0.3.4

Environment
Development

Target
Local Docker

[ Deploy ]
```

Deployment record must be created before execution begins.

---

# 34. Deployment domain

Conceptual model:

```text
Deployment

Id

ApplicationId

ReleaseId

EnvironmentId

TargetId

AgentId

Status

CreatedBy

CreatedAt

StartedAt

CompletedAt

PreviousDeploymentId
```

---

# 35. Deployment states

Initial states:

```text
Pending

Queued

WaitingForAgent

Running

Verifying

Succeeded

Failed

Cancelled

RolledBack
```

Rollback itself should create a new Deployment record rather than mutate historical records.

---

# 36. Deployment execution

Lifecycle:

```text
Create Deployment
      |
      v
Resolve Release
      |
      v
Resolve Environment
      |
      v
Resolve Target
      |
      v
Select Agent
      |
      v
Queue Deployment
      |
      v
Execute Steps
      |
      v
Verify Health
      |
      v
Succeeded / Failed
```

---

# 37. Deployment logs

Stream logs while deployment runs.

Example:

```text
Deployment #28

14:42:01 Preparing release 0.3.4

14:42:02 Pulling platform-api@sha256:...

14:42:05 Image available

14:42:05 Applying Docker Compose

14:42:09 Containers started

14:42:10 Waiting for health

14:42:16 API healthy

14:42:16 Deployment completed
```

Persist logs after completion.

---

# 38. Log separation

Distinguish:

```text
Platform deployment events

Agent execution output

Docker output

Verification output
```

The UI may present them as one timeline but preserve source metadata.

---

# 39. Deployment health

Deployment success must require explicit verification.

Do not mark success merely because:

```text
docker compose up
```

returned zero.

Support:

```text
container health

HTTP health endpoint
```

For dogfooding, use whichever the platform application supports.

---

# 40. HTTP health check

Example configuration:

```text
URL
http://localhost:8080/health

Expected Status
200

Timeout
60 seconds

Interval
2 seconds
```

A deployment remains:

```text
Verifying
```

until health succeeds or times out.

---

# 41. Container health

Where Docker image/container has a health check:

```text
docker inspect
```

may provide:

```text
healthy

unhealthy

starting
```

Use this as an optional verification step.

Do not assume every application has Docker health configuration.

---

# 42. Failed deployment

If any required step fails:

```text
Deployment = Failed
```

Display:

```text
Failed Step

Health Verification

Reason

HTTP endpoint did not become healthy within 60 seconds.
```

The previously healthy deployment should remain identifiable.

Do not automatically destroy history.

---

# 43. Environment state

Environment current state should only update after successful deployment.

Example:

Before:

```text
Development

Current Release
0.3.3
```

Attempt:

```text
Deploy 0.3.4
```

Failure:

```text
Current Release
0.3.3

Latest Attempt
0.3.4 Failed
```

Do not incorrectly claim 0.3.4 is current.

---

# 44. Deployment history

Environment page:

```text
History

#28    0.3.4    Failed       14:42

#27    0.3.3    Succeeded    13:10

#26    0.3.2    Succeeded    Yesterday
```

Selecting a deployment shows:

```text
Release

Commit

Pipeline

Agent

Target

Steps

Logs

Health

Duration
```

---

# 45. Release provenance

Release page should link back to:

```text
Commit

Change

Pipeline Run
```

where available.

Example:

```text
Release 0.3.4

Source

Change #52

Commit abc123

Pipeline Validate #240
```

Deploy should consume IDs/provenance contracts rather than directly querying Review/Pipelines databases.

---

# 46. Deployment provenance

Review may eventually display:

```text
Deployments

Development
0.3.4
```

Pipelines may display:

```text
Artifact

Used by Release 0.3.4
```

These are optional extensions.

No direct module implementation dependency is allowed.

---

# 47. Review extension

Deploy may contribute a Change extension through the platform extension mechanism.

Example:

```text
Change #52

Overview
Changes
Discussion
Reviewers
Checks
Deployments
```

Deployments tab may show:

```text
Release 0.3.4

Development
Deployed
```

Only show deployments traceable to that Change/commit.

---

# 48. Pipeline extension

Pipeline Run page may show:

```text
Deployments

Release 0.3.4

Development
Succeeded
```

Again, use extension contracts rather than hard-coded Deploy imports.

---

# 49. Rollback

Phase 3 MUST support rollback.

Rollback means:

> Create a new deployment using a previously successful Release.

Do not mutate history.

Example:

```text
Current

0.3.4

Previous Successful

0.3.3

[ Roll Back ]
```

---

# 50. Rollback flow

User selects:

```text
Rollback to 0.3.3
```

Platform creates:

```text
Deployment #29

Release
0.3.3

Reason
Rollback from failed/undesired 0.3.4
```

Then executes the standard deployment process.

---

# 51. Rollback result

After success:

```text
Development

Current Release
0.3.3

Status
Healthy
```

History:

```text
#29    0.3.3    Succeeded    Rollback

#28    0.3.4    Succeeded

#27    0.3.3    Succeeded
```

A rollback is simply another auditable deployment.

---

# 52. Rollback failure

Rollback may fail.

If it does:

```text
Rollback Deployment = Failed
```

Do not incorrectly change environment current release.

Display clearly that manual intervention may be required.

---

# 53. Deployment cancellation

Allow:

```text
[ Cancel Deployment ]
```

while:

```text
Queued

WaitingForAgent

Running
```

Cancellation flow:

```text
Control Plane
      |
      v
Agent
      |
      v
terminate step where safe
```

Final state:

```text
Cancelled
```

Do not present cancellation as failure.

---

# 54. Superseded deployments

Phase 3 does not require automatic deployment supersession.

If:

```text
0.3.4 deployment running

0.3.5 requested
```

initial behaviour may queue 0.3.5.

Avoid concurrent deployment to the same Application + Environment + Target.

Use a deployment lock.

---

# 55. Deployment locking

Only one active deployment should run for the same:

```text
Application
Environment
Target
```

combination.

Others remain:

```text
Queued
```

This avoids local Docker race conditions.

---

# 56. Deployment retries

Support:

```text
Retry Deployment
```

Retry creates a new deployment record using the same Release.

Historical failed deployment remains unchanged.

---

# 57. Release promotion model

Although Phase 3 only requires Development, preserve the model:

```text
Release
   |
   +-- Development
   |
   +-- Test
   |
   +-- Staging
   |
   +-- Production
```

The exact same immutable Release may be deployed to multiple environments.

Do not create a new build per environment.

---

# 58. Build once, deploy many

This is a hard architectural requirement.

Correct:

```text
Pipeline
   ↓
Artifact digest A
   ↓
Release 0.3.4
   ↓
Development
   ↓
Test
   ↓
Production
```

Incorrect:

```text
Build Dev artifact

Build Test artifact

Build Production artifact
```

A Release must retain immutable artifact references.

---

# 59. Local Docker deployment model

The initial dogfood target should run the platform independently of the development instance.

Example:

```text
Development Instance

localhost:5000
```

deployed dogfood instance:

```text
localhost:8080
```

Do not deploy over the control-plane instance that is currently orchestrating the deployment.

---

# 60. Dogfood topology

Recommended:

```text
Developer Machine

┌──────────────────────────┐
│ Active Dev Environment   │
│                          │
│ Platform.Api             │
│ Platform.Web             │
│ Database                 │
│ Pipeline Runner          │
│ Deployment Agent         │
└────────────┬─────────────┘
             |
             | Deploy
             v
┌──────────────────────────┐
│ Docker Dev Environment   │
│                          │
│ Released Platform        │
│ Release Database         │
│ supporting containers    │
└──────────────────────────┘
```

The deployment target is disposable/re-creatable.

---

# 61. Port isolation

The deployed instance must not conflict with the development control plane.

Use configurable ports.

Example:

```text
Development Platform
5000

Deployed Platform
8080
```

Do not hard-code these exact values if the repository already defines ports.

---

# 62. Docker resource naming

Deployment should use deterministic resource prefixes.

Example:

```text
platform-development-api

platform-development-web

platform-development-db
```

or Compose project:

```text
platform-development
```

Avoid collisions with:

```text
Pipeline Testcontainers

development containers

other environments
```

---

# 63. Persistent deployment state

Docker resources are runtime state.

The control plane database remains authoritative for:

```text
Deployment intent

Release identity

Deployment history

Environment current release

Target configuration

Agent identity
```

Do not rely solely on inspecting Docker to reconstruct deployment history.

---

# 64. Drift detection

Phase 3 should provide basic target drift awareness.

Example:

Platform expects:

```text
Release 0.3.4
```

but container is missing.

Environment may display:

```text
Degraded

Expected
0.3.4

Target
Container missing
```

Do not build full reconciliation/CD yet.

Basic detection is enough.

---

# 65. Manual reconcile

Optional Phase 3 action:

```text
[ Redeploy Current Release ]
```

This re-executes deployment using the current Release.

Useful if someone manually deletes a local container.

---

# 66. Continuous deployment is out of scope

Do NOT automatically deploy merely because:

```text
main pipeline passed
```

Phase 3 is deployment capability.

Automatic CD may be Phase 4 or a later capability.

Current path:

```text
Pipeline succeeds
      |
      v
Create Release
      |
      v
Deploy manually
```

---

# 67. Release creation convenience

To reduce friction during dogfooding, provide:

```text
Pipeline Run #240

Succeeded

Artifact
platform@sha256:...

[ Create Release ]
```

After creation:

```text
Release 0.3.4

[ Deploy to Development ]
```

This keeps the workflow explicit but fast.

---

# 68. Future automatic release policy

Do not implement now, but preserve room for:

```text
On main pipeline success:

Create Release automatically
```

and later:

```text
Deploy automatically to Development
```

These should be policies, not hard-coded coupling.

---

# 69. Deployment permissions

Register:

```text
deploy.read

deploy.release.create

deploy.execute

deploy.cancel

deploy.rollback

deploy.manage

deploy.environment.manage

deploy.target.manage

deploy.agent.read

deploy.agent.manage
```

All authorization is server-side.

---

# 70. Environment protection

Development environment requires no special approval in Phase 3.

Architecture must allow:

```text
Environment Policy
```

later.

Potential future:

```text
Production

Require Deployment Approval
```

Do not implement complex approval workflows yet.

---

# 71. Agent security

Deployment Agents execute privileged operations.

Treat them as trusted infrastructure.

Agent credentials must:

```text
be unique

be revocable

never appear in logs
```

The platform should clearly indicate that an agent with Docker capability can control Docker workloads on its host.

---

# 72. Container privileges

Do not automatically use:

```text
--privileged
```

unless a specific deployment explicitly requires it.

Run deployed workloads with the least privileges feasible.

---

# 73. Deployment secrets

Secrets must never be rendered into deployment logs.

When an environment variable is sourced from a secret:

```text
DATABASE_PASSWORD=********
```

not the actual value.

---

# 74. Agent protocol

Conceptual operations:

```text
Register

Heartbeat

RequestDeploymentWork

AcceptDeployment

ReportStepStarted

StreamLogs

ReportStepCompleted

ReportHealth

CompleteDeployment

ReportTargetStatus
```

Use explicit versionable DTOs.

Do not expose EF entities/domain internals over the protocol.

---

# 75. Agent reconnect

If agent disconnects mid-deployment:

```text
Deployment status = Running/Unknown
```

wait for a configured grace period.

If the agent reconnects:

```text
resume/report known state where possible
```

If it does not:

```text
Deployment = Failed or Unknown
```

with a clear reason.

Do not automatically retry deployment execution without knowing what target-side operations already occurred.

---

# 76. Unknown deployment state

The system should support an exceptional state:

```text
Unknown
```

for cases where agent connectivity is lost after side effects may have occurred.

This is preferable to falsely reporting:

```text
Failed
```

when the target may actually have updated.

Provide:

```text
Inspect Target

Redeploy

Rollback
```

where appropriate.

---

# 77. Deployment step idempotency

Where practical, deployment steps should be idempotent.

Examples:

```text
docker pull

docker compose up -d
```

are preferable to scripts that assume a clean target.

Do not claim guaranteed idempotency for arbitrary commands.

---

# 78. Deployment process snapshotting

A Deployment must retain the effective Deployment Process used.

If process configuration changes later:

```text
historical Deployment #28
```

must still display what actually ran.

Store:

```text
DeploymentProcessVersion

or immutable process snapshot
```

---

# 79. Release snapshotting

A Release is immutable.

Do not allow editing artifact digests after creation.

If artifacts change:

```text
create new Release
```

---

# 80. Environment configuration snapshot

Deployment should retain effective resolved configuration metadata.

Do not necessarily persist plaintext secrets.

Persist:

```text
variable names

non-secret resolved values where safe

secret reference IDs

process version

target version
```

This improves auditability.

---

# 81. Deployment UI navigation

When Deploy is enabled:

```text
Deploy
├── Applications
├── Releases
├── Environments
├── Deployments
├── Targets
└── Agents
```

A smaller initial UI may nest Targets/Agents under settings.

---

# 82. Applications page

Example:

```text
Applications

Platform

Current

Development
0.3.4
Healthy

Latest Release
0.3.5
Available
```

---

# 83. Releases page

Example:

```text
Releases

0.3.5
Available
Commit def456

0.3.4
Development
Commit abc123

0.3.3
Previously Deployed
```

Filters:

```text
Application

Deployment status
```

---

# 84. Environment page

Example:

```text
Development

Healthy

Current Release
0.3.4

Target
Local Docker

Last Deployment
#28
Succeeded

[ Deploy Release ]
[ Roll Back ]

History
...
```

---

# 85. Deployment page

Example:

```text
Deployment #28

Release
0.3.4

Environment
Development

Target
Local Docker

Agent
Rowan-PC

Status
Succeeded

Duration
24s

Steps

✓ Pull Images
✓ Apply Compose
✓ Start Containers
✓ Verify Health

Logs
...
```

---

# 86. Application overview extension

Project Overview may now show:

```text
Deployment

Development

Release
0.3.4

Status
Healthy
```

Do not show deployment cards when Deploy is disabled.

---

# 87. Pipeline extension

A successful Pipeline Run that produced a deployable artifact may show:

```text
Deployment

Artifact available for release

[ Create Release ]
```

This UI contribution should come from Deploy or a shared extension, not be hard-coded into Pipelines.

---

# 88. Review extension

For a merged Change associated with a Release:

```text
Deployments

Development
0.3.4
Healthy
```

Allow navigation:

```text
Change
  ↓
Release
  ↓
Deployment
```

---

# 89. Audit

Emit:

```text
deploy.application.created

deploy.release.created

deploy.environment.created

deploy.target.created

deploy.agent.registered

deploy.deployment.requested

deploy.deployment.started

deploy.deployment.completed

deploy.deployment.failed

deploy.deployment.cancelled

deploy.rollback.requested
```

Do not audit every log line.

---

# 90. Human activity

Environment timeline may show:

```text
14:42 Rowan deployed 0.3.4

14:42 Health check passed

15:13 Rowan deployed 0.3.5

15:17 Rowan rolled back to 0.3.4
```

This is separate from the system audit stream.

---

# 91. Persistence

Deploy owns:

```text
deploy.applications

deploy.releases

deploy.release_components

deploy.environments

deploy.targets

deploy.agents

deploy.processes

deploy.process_versions

deploy.deployments

deploy.deployment_steps

deploy.deployment_logs

deploy.health_results
```

Deploy MUST NOT directly query:

```text
review.*

pipelines.*
```

Use stable references/contracts/events.

---

# 92. APIs

Indicative API surface:

```text
/api/projects/{projectId}/deploy/applications

/api/projects/{projectId}/deploy/releases

/api/projects/{projectId}/deploy/environments

/api/projects/{projectId}/deploy/targets

/api/projects/{projectId}/deploy/deployments

/api/projects/{projectId}/deploy/deployments/{id}

/api/projects/{projectId}/deploy/deployments/{id}/cancel

/api/projects/{projectId}/deploy/deployments/{id}/rollback

/api/deploy/agents

/api/deploy/agents/register
```

Exact REST layout may differ.

Preserve domain boundaries.

---

# 93. Module disabled behaviour

When Deploy is disabled:

```text
Deploy navigation disappears

Deploy routes disappear

Deploy API disappears

Deploy event handlers disappear

Deployment Agents cannot register

Deploy extensions disappear
```

Review and Pipelines continue functioning normally.

---

# 94. Failure handling

Handle clearly:

```text
No Deployment Agent

Agent offline

Docker unavailable

Artifact unavailable

Artifact digest mismatch

Image pull failure

Compose failure

Container startup failure

Port conflict

Health timeout

Health failure

Agent disconnect

Deployment cancellation

Rollback failure
```

Differentiate:

```text
Configuration Failure

Artifact Failure

Target Failure

Execution Failure

Health Failure

Connectivity Failure
```

---

# 95. Artifact digest verification

Where possible, verify the artifact identity before/after acquisition.

If expected:

```text
sha256:abc
```

but obtained artifact does not match:

```text
Deployment fails
```

Do not deploy an artifact that does not match the Release.

---

# 96. Target inspection

The Docker provider should be able to inspect:

```text
containers

image IDs/digests

health

running/stopped state
```

This supports environment health and basic drift detection.

---

# 97. Testing

## Unit tests

Cover:

```text
Release immutability

deployment state transitions

deployment locking

agent selection

process snapshotting

environment current-release calculation

rollback selection

health evaluation

artifact identity

permission checks
```

## Integration tests

Cover:

```text
Deploy module enabled

Deploy module disabled

Application creation

Release creation

Environment creation

Target creation

Agent registration

Deployment queue

Step progression

Successful deployment

Failed deployment

Health failure

Cancellation

Rollback

Environment state updates

Audit events
```

---

# 98. Agent integration tests

Use fake/test target executors for deterministic control-plane tests.

Agent tests should cover:

```text
registration

heartbeat

work acquisition

step execution

log streaming

success

failure

cancellation

reconnect
```

---

# 99. Real Docker smoke test

Provide a non-default real Docker test.

Deploy a tiny container:

```text
nginx
```

or a purpose-built test image.

Verify:

```text
container starts

HTTP endpoint responds

deployment succeeds

container can be replaced

rollback succeeds
```

This proves actual Docker integration.

---

# 100. Platform dogfood deployment

Configure the platform itself as an Application.

Example:

```text
Application

Platform
```

Pipeline produces:

```text
platform image
```

or:

```text
platform compose bundle
```

Create:

```text
Release 0.3.0
```

Target:

```text
Local Docker
```

Environment:

```text
Development
```

---

# 101. Dogfood acceptance scenario

This is the primary Phase 3 acceptance test.

## Starting state

```text
Phase 1 Review works.

Phase 2 Pipelines works.

Local Pipeline Runner is online.

Local Deployment Agent is online.

Development Docker target exists.
```

---

# 102. Build the feature

Create:

```text
dogfood/phase-three-deploy
```

Develop the Phase 3 implementation.

Push.

Create Change.

Run:

```text
Build

Unit

Integration

E2E
```

All required checks pass.

Approve.

Merge.

---

# 103. Package the platform

Run the main branch pipeline.

Pipeline must produce:

```text
deployable Platform artifact
```

Prefer:

```text
OCI image digest
```

or:

```text
Compose deployment bundle referencing immutable images
```

Verify artifact exists.

---

# 104. Create dogfood Release

From successful pipeline:

```text
[ Create Release ]
```

Create:

```text
Release 0.3.0
```

Verify:

```text
Commit correct

Pipeline correct

Artifact digest correct
```

---

# 105. Deploy dogfood Release

Select:

```text
Environment
Development

Target
Local Docker
```

Deploy.

Verify:

```text
Deployment queued

Agent receives work

Artifact acquired

Docker resources updated

Application starts

Health passes

Deployment succeeds
```

---

# 106. Verify environment

Environment UI:

```text
Development

Current Release
0.3.0

Status
Healthy

Target
Local Docker
```

Open the deployed platform URL.

Verify the deployed application actually works.

---

# 107. Deploy second Release

Make another small Change.

Run through:

```text
Review

Pipelines

Merge

Release
```

Create:

```text
0.3.1
```

Deploy 0.3.1.

Verify:

```text
Development current = 0.3.1

0.3.0 remains in history
```

---

# 108. Rollback acceptance

Select:

```text
Roll Back
```

Choose:

```text
0.3.0
```

Verify:

```text
new Deployment created

0.3.0 artifact reused

deployment executes

health succeeds

Development current = 0.3.0
```

Deployment history must show:

```text
0.3.0 rollback

0.3.1 deployment

0.3.0 original deployment
```

---

# 109. Failure acceptance

Deploy a deliberately broken Release or configuration.

Verify:

```text
deployment fails

health does not pass

logs explain failure

environment does not incorrectly mark broken Release current

previous successful Release remains identifiable

rollback/redeploy remains possible
```

---

# 110. Phase 3 completion criteria

Phase 3 is complete only when:

```text
Deploy is genuinely modular.

Applications exist.

Pipeline artifacts can become Releases.

External artifacts could theoretically become Releases.

Releases are immutable.

Artifact identities are immutable/digest-based.

Development environment exists.

Deployment target exists.

Local Deployment Agent can register.

Agent health is visible.

Docker target works.

Deployment process exists.

Deployment steps execute.

Deployment logs stream.

Docker containers update.

Health verification runs.

Failed health blocks successful deployment.

Environment current Release updates only after success.

Deployment history persists.

Cancellation works.

Retry works.

Rollback creates a new deployment.

Rollback uses an old immutable Release.

Basic drift can be detected.

Deploy extensions appear in Review/Pipelines without direct dependencies.

Deploy can be disabled without breaking Review or Pipelines.

The platform can deploy its own validated build locally.

The platform can roll itself back locally.
```

---

# 111. Explicit future deferrals

Do NOT implement during Phase 3:

```text
Automatic Continuous Deployment

Production environments

Production approvals

Multi-stage promotion policies

Kubernetes provider

SSH provider

Cloud providers

Blue/Green

Canary

Traffic weighting

Multi-site deployment

Deployment windows

Advanced secrets providers

Infrastructure-as-Code orchestration

Fleet management
```

These can build on the Phase 3 Deploy domain later.

---

# 112. Natural next capabilities

After Phase 3, possible directions include:

```text
Automatic Dev deployment

Test/Staging/Production environments

Deployment approvals

Release promotion

Remote Docker agents

Kubernetes deployment

Environment variables/secrets improvements

Artifact registry

Release notes

Jira deployment metadata

Production governance

Air-gapped deployment workflows
```

Do not implement these prematurely.

---

# 113. Implementation order

Implement Phase 3 in this order:

```text
1. Deploy module skeleton

2. Application domain

3. ArtifactReference contract finalisation

4. Release domain

5. Release creation from Pipeline output

6. Environment domain

7. Deployment Target domain

8. Deployment Agent registration

9. Agent heartbeat

10. Deployment Process model

11. Deployment Step model

12. Deployment queue

13. Agent work assignment

14. Docker provider

15. Local Docker target

16. Deployment logs

17. Deployment state transitions

18. Health checks

19. Environment current-release calculation

20. Deployment history

21. Cancellation

22. Retry

23. Rollback

24. Basic drift inspection

25. Review extension

26. Pipeline extension

27. Real Docker smoke test

28. Platform dogfood Release

29. Deploy platform locally

30. Deploy second Release

31. Roll back platform

32. Dogfood Phase 3 using the platform itself
```

Do not move into advanced CD functionality until the entire dogfood scenario works reliably.

---

# 114. Guiding implementation rule

The implementation agent should optimise for:

> A developer can take a build that passed Review and Pipelines, create an immutable Release, deploy it to local Docker, verify that it is healthy, and return to the previous Release if necessary.

Do not build a universal deployment platform during Phase 3.

Build the smallest complete Deploy product capable of deploying the platform itself.

The architectural boundary is:

```text
Review
    |
Pipelines
    |
ArtifactReference
    |
Deploy
    |
Deployment Agent
    |
Docker
```

The arrows mean integration.

They do not mean implementation dependency.

At the end of Phase 3, the platform should be capable of using itself for its complete initial development lifecycle:

```text
Develop
   ↓
Push
   ↓
Review
   ↓
Test
   ↓
Merge
   ↓
Package
   ↓
Release
   ↓
Deploy
   ↓
Verify
   ↓
Rollback if necessary
```

The final Phase 3 product must therefore be capable of:

> **reviewing, validating, releasing, deploying, and rolling back its own development using its own Review, Pipelines, and Deploy modules.**
