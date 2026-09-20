# Phase 2 — Working Local Pipelines for Review

## 1. Objective

Extend the Phase 1 Review product with a working Pipelines module capable of validating Changes using a runner executing locally on the developer's machine.

Phase 1 already provides:

```text
Local Working Repository
        |
        | git push
        v
      GitHub
        |
        v
 Local Platform
        |
        v
      Review
```

Phase 2 extends this to:

```text
Local Working Repository
        |
        | git push
        v
      GitHub
        |
        v
      Change
        |
        | ChangeOpened / ChangeUpdated
        v
     Pipelines
        |
        v
   Local Runner
        |
        +-- Build
        |
        +-- Unit Tests
        |
        +-- Integration Tests
        |
        +-- E2E Tests
        |
        v
      Checks
        |
        v
      Review
```

The developer should be able to:

> Push a branch, open/update a Change, have the platform automatically execute the project's tests locally, see the results inside Review, and only merge when required checks pass.

---

# 2. Phase 2 success condition

Phase 2 is complete when this workflow works end-to-end:

```text
1. Run the platform locally.

2. Run/register a local pipeline runner.

3. Open an existing Phase 1 project.

4. Configure a validation pipeline.

5. Create a feature branch locally.

6. Make changes.

7. Commit.

8. Push to GitHub.

9. Create/open a Change.

10. Platform automatically starts a pipeline for the Change HEAD commit.

11. Local runner receives the pipeline.

12. Runner creates an isolated workspace.

13. Runner checks out the exact Change commit.

14. Runner executes:

      Build
      Unit Tests
      Integration Tests
      E2E Tests

15. Logs stream into the platform.

16. Test results are parsed.

17. Review displays Checks.

18. Failed checks block merge.

19. Push a fix.

20. Previous pipeline becomes superseded/cancelled where appropriate.

21. New pipeline executes against the new HEAD commit.

22. All required checks pass.

23. Merge requirements become satisfied.

24. Merge through Review.

25. Continue developing the platform using this workflow.
```

---

# 3. Scope

Implement:

```text
Pipelines Module

Pipeline Definitions
Triggers
Pipeline Runs
Jobs
Steps
Local Runner
Runner Registration
Runner Capabilities
Job Scheduling
Workspace Isolation
Repository Checkout
Process Execution
Docker Access
Logs
Cancellation
Checks
Test Results
Run Artifacts
Review Integration
Merge Requirements
Audit
```

Do not implement:

```text
Deployment / CD
Environments
Releases
Production promotion

Cloud-hosted runners
Runner autoscaling
Kubernetes runners

Large distributed scheduler
Multi-region infrastructure

Package registry
Container registry

Advanced pipeline marketplace

Advanced reusable templates

Matrix builds

Complex DAG scheduler

Secrets vault

GitHub Actions compatibility

Full YAML compatibility with another CI provider
```

---

# 4. Architectural invariants

Pipelines is an independently enabled module.

It MUST NOT depend directly on Review.

Review MUST NOT depend directly on Pipelines.

Integration occurs through:

```text
Events
Provider contracts
Checks
UI extension points
```

Conceptually:

```text
Review
   |
   | ChangeOpened
   | ChangeUpdated
   v
 Event Bus
   |
   v
Pipelines
   |
   | CheckUpdated
   v
ICheckProvider
   |
   v
Review
```

If Review is absent:

```text
Pipelines
```

must still support:

```text
manual execution
push triggers
scheduled execution later
```

If Pipelines is absent:

```text
Review
```

must continue functioning exactly as Phase 1.

---

# 5. Module responsibilities

Pipelines owns:

```text
Pipeline Definitions
Pipeline Runs

Stages
Jobs
Steps

Triggers

Runner Registration
Runner Selection
Runner Heartbeats

Job Execution State

Logs

Test Results

Pipeline Run Artifacts

Checks produced by pipelines
```

Review owns:

```text
Changes
Reviews
Comments
Approval policy
Merge policy
```

Core owns:

```text
Projects
Users
Permissions
Audit
Events
Capabilities
Module runtime
```

---

# 6. Core pipeline domain

Define the following domain concepts:

```text
PipelineDefinition

PipelineTrigger

PipelineRun

PipelineStage

PipelineJob

PipelineStep

Runner

RunnerCapability

JobAssignment

PipelineLog

Check

TestRun
TestSuite
TestCase

RunArtifact
```

Do not encode Unit, Integration, or E2E as special pipeline job types.

They are ordinary jobs whose commands happen to execute tests.

---

# 7. Pipeline definition

A pipeline describes:

```text
Name

Triggers

Jobs

Job ordering

Commands

Environment variables

Runner requirements

Timeouts

Required-check behaviour
```

Example conceptual definition:

```text
Pipeline: Validate

Trigger:
    Change Opened
    Change Updated

Jobs:

    Build
        dotnet build

    Unit
        dotnet test Tests.Unit

    Integration
        dotnet test Tests.Integration

    E2E
        dotnet test Tests.E2E
```

---

# 8. Pipeline configuration approach

For Phase 2, support a platform-owned pipeline definition.

A pipeline may initially be created and edited through the UI.

Persist the definition in the platform database.

Do NOT require the introduction of a complex pipeline-as-code format to complete Phase 2.

The domain model MUST permit future serialisation to repository-owned configuration such as:

```text
.platform/pipeline.yml
```

or equivalent.

The execution engine must not depend on the UI representation.

---

# 9. Default .NET pipeline

The platform may offer a convenience template:

```text
.NET Validation
```

Example generated jobs:

```text
Restore

Build

Unit Tests

Integration Tests

E2E Tests
```

The developer must be able to edit/delete/reorder these.

Do not hard-code .NET concepts into the execution engine.

---

# 10. Pipeline triggers

Phase 2 must support:

```text
Manual

Change Opened

Change Updated

Push
```

Optional if straightforward:

```text
Change Merged
```

Future triggers may include:

```text
Tag
Schedule
Webhook
Release
Deployment
```

---

# 11. Change-trigger behaviour

When Review emits:

```text
ChangeCreated
```

or:

```text
ChangeUpdated
```

Pipelines determines whether any pipeline definitions subscribe to that event.

Example:

```text
Change #42

HEAD:
abc123
```

produces:

```text
Pipeline Run #180

Source:
Change #42

Commit:
abc123
```

Every Change-triggered run MUST be tied to an immutable commit SHA.

---

# 12. Commit correctness

A pipeline MUST validate the exact commit being reviewed.

Never execute against:

```text
the developer's active working directory
whatever branch happens to be checked out locally
uncommitted changes
latest remote branch without recording SHA
```

Pipeline run identity includes:

```text
Repository
Commit SHA
Change ID where applicable
Pipeline Definition Version
```

---

# 13. Runner architecture

Pipeline jobs execute outside the central application process.

Architecture:

```text
Platform Control Plane
        |
        | authenticated runner protocol
        v
   Local Runner
        |
        v
 Isolated Workspace
        |
        v
  Job Processes
```

The runner may initially be:

```text
separate .NET worker process
```

or:

```text
separate container/process launched locally
```

It must not execute arbitrary pipeline commands inside `Platform.Api`.

---

# 14. Local runner

Phase 2 requires one first-class local runner.

Example UI:

```text
Runner

Name
Rowan-PC

Status
Online

Operating System
Linux / WSL / Windows

Capabilities
dotnet
docker
playwright

Concurrency
1

Last Seen
2 seconds ago
```

---

# 15. Runner registration

Provide a runner registration workflow.

Example:

```text
Settings
    Pipelines
        Runners

[ Add Runner ]
```

Generate:

```text
Runner Registration Token
```

Runner starts with configuration such as:

```text
Platform URL
Registration Token
Runner Name
```

After registration, exchange the temporary token for durable runner credentials.

---

# 16. Runner authentication

Runner connections must be authenticated.

Registration tokens should:

```text
expire
be single-use where practical
be revocable
```

Runner credentials should:

```text
be unique per runner
be revocable
never appear in logs
```

Do not trust arbitrary localhost processes merely because the platform is running locally.

---

# 17. Runner heartbeat

Runner periodically reports:

```text
Online
Busy
Offline

Capabilities

Current job count

Version

Last heartbeat
```

If heartbeats stop beyond a timeout:

```text
Runner = Offline
```

Jobs assigned to a dead runner should eventually be marked:

```text
Lost
```

or:

```text
Failed
```

with a clear reason.

---

# 18. Runner capabilities

A runner advertises capabilities.

Example:

```text
dotnet
docker
playwright
linux
windows
node
```

Pipeline jobs may require capabilities:

```text
Unit:
    requires:
        dotnet

Integration:
    requires:
        dotnet
        docker

E2E:
    requires:
        dotnet
        docker
        playwright
```

Scheduler selects an eligible runner.

For Phase 2 there may only be one runner, but preserve this contract.

---

# 19. Runner concurrency

Initial default:

```text
Concurrency = 1
```

The runner executes one job at a time unless explicitly configured otherwise.

Architecture should support:

```text
Concurrency > 1
```

later.

Do not implement complex resource-aware scheduling in Phase 2.

---

# 20. Isolated workspaces

Every Pipeline Run receives an isolated workspace.

Example:

```text
.runner/
    work/
        run-180/
            source/
            artifacts/
            temp/
```

Do NOT execute CI directly against:

```text
C:\Development\Platform
```

or the user's active checkout.

This prevents:

```text
uncommitted-change leakage
branch changes
test-generated files polluting development
CI accidentally deleting developer files
```

---

# 21. Repository checkout

Runner obtains source using the project's source provider.

For Phase 2 with GitHub:

```text
GitHub repository
      |
      v
Runner clone/fetch
      |
      v
checkout exact SHA
```

Optimisation MAY maintain a local bare repository cache.

Correctness is more important than performance.

Before execution:

```text
git rev-parse HEAD
```

must resolve to the Pipeline Run commit SHA.

---

# 22. Workspace lifecycle

Lifecycle:

```text
Create workspace

Fetch source

Checkout SHA

Execute jobs

Collect results

Collect artifacts

Finalize run

Clean workspace
```

On failure/cancellation:

```text
attempt cleanup
```

Runner configuration may optionally preserve failed workspaces for debugging.

Default should clean them.

---

# 23. Pipeline execution model

Phase 2 may use sequential job execution.

Example:

```text
Restore
   |
   v
Build
   |
   v
Unit
   |
   v
Integration
   |
   v
E2E
```

A failed required job stops later dependent jobs unless configured otherwise.

Do not implement a general DAG engine yet.

The domain model should permit dependencies later.

---

# 24. Job lifecycle

Job states:

```text
Queued

WaitingForRunner

Assigned

Running

Succeeded

Failed

Cancelled

Skipped

Lost
```

Record:

```text
queued time
start time
completion time
duration
runner
exit code
failure reason
```

---

# 25. Step lifecycle

A job consists of one or more ordered steps.

Example:

```text
Job: Unit Tests

Step 1
dotnet restore

Step 2
dotnet test Tests.Unit
```

Step state:

```text
Pending
Running
Succeeded
Failed
Cancelled
Skipped
```

---

# 26. Command execution

Phase 2 must support shell/process commands.

Example:

```text
dotnet restore

dotnet build --no-restore

dotnet test Tests.Unit --no-build
```

Runner captures:

```text
stdout
stderr
exit code
duration
```

Non-zero exit code fails the step unless explicitly configured otherwise.

---

# 27. Shell selection

Runner should use an explicit shell appropriate to the environment.

Potential values:

```text
bash
pwsh
cmd
```

Do not rely on ambiguous system defaults.

The pipeline definition may specify a shell where necessary.

---

# 28. Environment variables

Support:

```text
pipeline-level variables

job-level variables

step-level variables
```

Predefined variables should include:

```text
PLATFORM_PROJECT_ID

PIPELINE_ID

PIPELINE_RUN_ID

COMMIT_SHA

SOURCE_BRANCH

TARGET_BRANCH

CHANGE_ID
```

where relevant.

Do not expose secrets through logs.

---

# 29. Basic secrets

Phase 2 only requires a minimal secret mechanism if tests require secrets.

Secrets should be:

```text
encrypted at rest

injected at execution time

redacted from logs where practical
```

Do not build a full Vault replacement.

The architecture should allow a future:

```text
ISecretProvider
```

---

# 30. Docker access

Integration and E2E tests may require Docker.

The runner must support jobs requiring:

```text
docker
```

On a developer machine this may mean access to:

```text
Docker Desktop

Docker Engine

WSL Docker socket
```

Exact transport depends on host configuration.

---

# 31. Testcontainers compatibility

The runner does not need to understand the topology of integration or E2E tests.

Example:

```text
Runner
    |
    v
dotnet test Tests.Integration
    |
    v
Testcontainers
    |
    +-- Postgres
    +-- API
    +-- dependency services
```

Likewise:

```text
Runner
    |
    v
dotnet test Tests.E2E
    |
    v
Testcontainers
    |
    +-- frontend
    +-- backend
    +-- identity
    +-- databases
    +-- simulated dependencies
```

The pipeline engine only needs to provide:

```text
source
process execution
Docker access
timeouts
logs
artifacts
```

---

# 32. Docker cleanup

Pipeline tests may create containers/networks/volumes.

Tests remain responsible for normal Testcontainers cleanup.

Runner should additionally record enough metadata to help identify abandoned resources where possible.

Do not indiscriminately delete every Docker resource on the machine.

Future runner isolation may improve this.

---

# 33. Pipeline logs

Logs must stream while a job runs.

UI:

```text
E2E Tests
Running

14:04:21 Starting dotnet test...
14:04:23 Restoring...
14:04:29 Starting containers...
14:04:34 Keycloak ready
14:04:41 API ready
...
```

Logs should support:

```text
live append
timestamps
stdout/stderr distinction where practical
final persisted history
```

---

# 34. Log storage

Persist completed logs.

Do not require the runner to remain online to view historic logs.

Log storage may initially use:

```text
database for metadata

filesystem/object storage for larger logs
```

Avoid storing arbitrarily huge logs in a single relational database field.

---

# 35. Log limits

Protect the control plane from runaway output.

Support configurable limits:

```text
maximum log size per step/job

maximum line length

maximum run duration
```

On exceeding a limit:

```text
truncate safely
display warning
```

Do not silently discard the existence of truncation.

---

# 36. Cancellation

User must be able to:

```text
Cancel Pipeline
```

or:

```text
Cancel Job
```

Control plane signals runner.

Runner should attempt graceful termination, then force termination after a timeout.

Final state:

```text
Cancelled
```

Cancellation must not appear as a test failure.

---

# 37. Superseded Change runs

When a Change receives a new HEAD commit:

```text
Commit A
    |
pipeline running

new push

Commit B
```

the old run is no longer authoritative for merge eligibility.

At minimum:

```text
Commit A checks do not apply to Commit B
```

Preferred Phase 2 behaviour:

```text
automatically cancel older queued/running Change validation
```

when superseded.

This may be configurable later.

---

# 38. Checks

Pipelines publishes provider-neutral Checks.

Define:

```csharp
public interface ICheckProvider
{
    Task<IReadOnlyList<CheckResult>> GetChecksAsync(
        CheckContext context,
        CancellationToken cancellationToken);
}
```

A Check includes:

```text
ID

Name

Status

Conclusion

Commit SHA

Source

StartedAt

CompletedAt

Details URL / resource ID
```

---

# 39. Check status

Status:

```text
Queued

Running

Completed
```

Conclusion:

```text
Succeeded

Failed

Cancelled

Skipped

Neutral
```

Review should not infer success from pipeline internals.

It consumes Checks.

---

# 40. Check mapping

Pipeline jobs may publish checks.

Example:

```text
Job                  Check

Build                Build

Unit                  Unit Tests

Integration           Integration Tests

E2E                   E2E Tests
```

Only configured jobs need to become externally visible checks.

---

# 41. Review integration

Phase 1 reserved a Change extension point.

Phase 2 registers:

```text
Checks
```

without modifying Review's implementation.

Change UI becomes:

```text
Overview
Changes
Discussion
Reviewers
Checks
```

---

# 42. Checks tab

Example:

```text
Checks

✓ Build
  Passed
  14s

✓ Unit Tests
  184 passed
  8s

✓ Integration Tests
  41 passed
  51s

○ E2E Tests
  Running
  1m 12s
```

Failed:

```text
✗ E2E Tests

18 passed
1 failed

Duration
2m 41s

[ View Run ]
```

---

# 43. Checks on overview

Review Overview should show a compact summary:

```text
Checks

✓ Build
✓ Unit Tests
✓ Integration Tests
○ E2E Tests
```

The full details live in the Checks extension.

---

# 44. Commit binding

Checks apply to:

```text
specific Commit SHA
```

not merely:

```text
Change ID
```

Review merge eligibility must compare:

```text
Current Change HEAD SHA
```

against:

```text
Check Commit SHA
```

Stale successful checks must never allow a newer untested commit to merge.

---

# 45. Required checks

Phase 2 introduces a basic Review requirement:

```text
Require selected checks to succeed.
```

Example configuration:

```text
Required Checks

Build

Unit Tests

Integration Tests

E2E Tests
```

Merge policy:

```text
✓ Required approval

✓ No Changes Requested

✓ Build

✓ Unit Tests

✓ Integration Tests

○ E2E Tests

Merge blocked
```

---

# 46. Required-check evaluation

A required check passes only if:

```text
check exists

check belongs to current HEAD SHA

check completed

conclusion = Succeeded
```

A missing check is:

```text
Pending / Not Run
```

not successful.

---

# 47. Pipeline result

Pipeline Run conclusion:

```text
Succeeded

Failed

Cancelled

PartiallySucceeded / Neutral
```

For Phase 2, simple overall result logic is acceptable:

```text
any required job failed
    => Failed

all required jobs succeeded
    => Succeeded

cancelled
    => Cancelled
```

---

# 48. Test results

Test output should become structured data where possible.

Define:

```text
TestRun

TestSuite

TestCase
```

TestCase contains:

```text
Name

Suite

Outcome

Duration

Failure Message

Stack Trace

Output where available
```

Outcome:

```text
Passed

Failed

Skipped
```

---

# 49. .NET test results

Phase 2 should support parsing `.trx`.

Pipeline commands should be able to produce:

```text
TestResults/*.trx
```

Example:

```text
dotnet test Tests.Unit \
  --logger "trx;LogFileName=unit.trx"
```

The runner/platform then parses the result.

Exact command syntax should account for the runner shell.

---

# 50. Test summary UI

Job view:

```text
Unit Tests

Passed:   184
Failed:     0
Skipped:    3

Duration:
8.4s
```

Failure view:

```text
OperatorCanStartMachine

Failed

Expected:
Running

Actual:
Stopped

Duration:
1.82s

Stack Trace
...
```

---

# 51. Test result abstraction

The platform test model must not be `.trx`-specific.

Future adapters may support:

```text
JUnit XML

NUnit XML

Jest

Playwright

pytest
```

`.trx` is simply the first parser.

---

# 52. Pipeline artifacts

A pipeline job may publish files as Run Artifacts.

These are NOT yet a general package registry.

Examples:

```text
TRX files

coverage reports

Playwright traces

screenshots

logs

diagnostic dumps
```

Define:

```text
RunArtifact

ID

PipelineRunId

JobId

Name

Path / StorageReference

ContentType

Size

CreatedAt
```

---

# 53. Artifact collection

Pipeline job configuration may declare paths:

```text
artifacts:

    TestResults/**

    playwright-traces/**

    screenshots/**
```

Runner uploads matching files after job execution.

Artifact upload failure should not necessarily fail the job unless configured as required.

---

# 54. Artifact UI

Job/Run page:

```text
Artifacts

unit.trx

integration.trx

playwright-trace.zip

failure-screenshot.png
```

Allow download/open where supported.

---

# 55. E2E diagnostics

When E2E fails, the developer should ideally be able to reach diagnostic artifacts directly from Review:

```text
E2E Tests        Failed

Failed Tests
OperatorCanStartMachine

Artifacts
[ Playwright Trace ]
[ Failure Screenshot ]
[ Test Results ]
```

This is an important dogfooding usability target.

---

# 56. Pipeline list

Project navigation when Pipelines is enabled:

```text
Pipelines
├── Pipelines
├── Runs
└── Runners
```

---

# 57. Pipeline definitions page

Display:

```text
Validate

Triggers
Change Opened
Change Updated

Jobs
Build
Unit
Integration
E2E

Status
Enabled
```

Actions:

```text
Run

Edit

Disable

Delete
```

---

# 58. Pipeline runs page

Display:

```text
Run       Pipeline    Source        Commit     Result

#180      Validate    Change #42    abc123     Running

#179      Validate    Change #42    def456     Cancelled

#178      Validate    Change #39    777aaa     Passed
```

Filters:

```text
pipeline

status

branch

Change
```

---

# 59. Pipeline run page

Example:

```text
Validate #180

Change #42
feature/pipeline-runner → main

Commit
abc123

Runner
Rowan-PC

Status
Running

Jobs

✓ Build              14s

✓ Unit                8s

✓ Integration        51s

○ E2E              1m 23s
```

Selecting a job shows:

```text
Steps

Logs

Tests

Artifacts
```

---

# 60. Manual run

Pipeline definition page supports:

```text
[ Run Pipeline ]
```

User selects:

```text
Branch / Commit
```

Default:

```text
project default branch
```

Manual runs do not need an associated Change.

---

# 61. Push trigger

Push events may originate from the source provider.

For Phase 2, if GitHub webhooks are not yet configured, push detection may occur through the existing synchronization mechanism.

However, Change-triggered pipelines are the primary acceptance path.

Do not block Phase 2 solely on production-grade webhook infrastructure.

---

# 62. Pipeline definition versioning

A Pipeline Run must retain the effective definition used for that run.

If the definition changes later:

```text
historic runs remain reproducible/auditable
```

Persist:

```text
PipelineDefinitionVersion

or immutable snapshot
```

Do not render historical runs using the latest mutable definition.

---

# 63. Pipeline permissions

Register:

```text
pipelines.read

pipelines.run

pipelines.cancel

pipelines.manage

pipelines.runner.read

pipelines.runner.manage
```

Runner authentication is separate from user RBAC.

---

# 64. Audit

Emit Core audit events:

```text
pipeline.created

pipeline.updated

pipeline.deleted

pipeline.run.requested

pipeline.run.started

pipeline.run.cancelled

pipeline.run.completed

runner.registered

runner.revoked
```

Do not audit every individual log line.

---

# 65. Human activity

Where relevant, Review may show:

```text
Validation pipeline started

Validation pipeline passed

Validation pipeline failed
```

Do not flood the Change activity stream with every internal job transition.

---

# 66. Persistence

Pipelines owns logical storage such as:

```text
pipelines.definitions

pipelines.definition_versions

pipelines.runs

pipelines.jobs

pipelines.steps

pipelines.runners

pipelines.assignments

pipelines.checks

pipelines.test_runs

pipelines.test_suites

pipelines.test_cases

pipelines.artifacts
```

Review must not query these tables.

Review consumes `ICheckProvider`.

---

# 67. APIs

Indicative API surface:

```text
/api/projects/{projectId}/pipelines

/api/projects/{projectId}/pipelines/{pipelineId}

/api/projects/{projectId}/pipelines/{pipelineId}/runs

/api/projects/{projectId}/pipeline-runs/{runId}

/api/projects/{projectId}/pipeline-runs/{runId}/cancel

/api/projects/{projectId}/pipeline-runs/{runId}/jobs

/api/projects/{projectId}/pipeline-runs/{runId}/logs

/api/projects/{projectId}/pipeline-runs/{runId}/tests

/api/projects/{projectId}/pipeline-runs/{runId}/artifacts

/api/pipelines/runners

/api/pipelines/runners/register
```

Runner communication may use:

```text
HTTP long polling

WebSocket

SignalR
```

Choose the simplest reliable mechanism consistent with the existing application.

---

# 68. Runner protocol

Runner requires operations conceptually equivalent to:

```text
Register

Heartbeat

RequestWork

AcceptJob

StreamLogs

ReportStep

UploadTestResults

UploadArtifact

CompleteJob
```

Do not expose internal EF/domain objects across the runner protocol.

Use explicit versionable DTO contracts.

---

# 69. Runner connectivity

For Phase 2 everything may run on the same laptop.

Still design connectivity as:

```text
Runner initiates connection to Platform
```

rather than requiring the platform to connect inbound to the runner.

This matches future:

```text
on-prem

industrial

remote

customer-site
```

runner scenarios.

---

# 70. Security boundary

Pipeline execution means executing repository-controlled commands.

Treat the runner as privileged infrastructure.

The control plane must not assume runner commands are safe.

For Phase 2:

```text
runner is explicitly trusted by the developer
```

Display a warning during runner registration that pipeline commands can execute arbitrary code with the runner's permissions.

Do not claim strong sandboxing that does not exist.

---

# 71. Secrets and untrusted Changes

Do not automatically expose sensitive secrets to arbitrary Change pipelines.

The initial dogfooding environment is trusted, but architecture should support:

```text
trusted branch

trusted contributor

protected secret

protected pipeline
```

later.

Avoid baking unconditional secret access into execution.

---

# 72. Timeouts

Support configurable:

```text
step timeout

job timeout

pipeline timeout
```

Provide sensible defaults.

A hung integration/E2E test must not block the runner forever.

---

# 73. Retry

Phase 2 should support:

```text
Retry Run
```

Optional:

```text
Retry Failed Job
```

A retry creates a new execution record.

Do not mutate historical results.

---

# 74. Local resource awareness

Do not attempt sophisticated scheduling based on:

```text
CPU

RAM

Docker resource availability
```

for Phase 2.

Runner may optionally report:

```text
CPU count

memory

OS
```

for diagnostic display.

---

# 75. Module disabled behaviour

When Pipelines is disabled:

```text
Pipelines navigation disappears

/api/pipelines/* disappears

Pipeline triggers do not run

Runner registration is unavailable

Pipeline event handlers are absent

Checks extension contributed by Pipelines disappears
```

Review continues functioning.

Existing historical Review data remains intact.

---

# 76. Review module isolation

Review must not contain code such as:

```csharp
_pipelineService.GetRun(...)
```

Instead:

```text
Review

consumes

ICheckProvider
```

Likewise Pipelines should react to:

```text
ChangeCreated
ChangeUpdated
```

rather than invoking Review services.

---

# 77. Change extension registration

Pipelines registers a Review resource extension conceptually equivalent to:

```text
Resource:
    review.change

Extension:
    checks

Placement:
    tab / overview summary
```

If Pipelines is absent:

```text
extension is absent
```

No conditional Pipelines imports should live inside Review UI code.

---

# 78. Failure handling

Handle clearly:

```text
No runner available

Runner offline

Runner disconnected mid-job

Checkout failed

Commit missing

Command not found

Docker unavailable

Step timeout

Process crashed

Test failure

Test-result parse failure

Artifact upload failure

Cancellation failure

Platform connection lost
```

Distinguish:

```text
Infrastructure Failure

Pipeline Failure

Test Failure

Cancellation
```

where practical.

---

# 79. Runner disconnect

If runner disconnects while executing:

```text
mark assignment uncertain

wait configured grace period
```

If it does not reconnect:

```text
job = Lost
pipeline = Failed
```

Do not silently re-run potentially non-idempotent jobs in Phase 2.

---

# 80. Test-result parsing failure

If:

```text
dotnet test exits successfully
```

but `.trx` parsing fails:

```text
job execution result remains based on command exit code

test-report UI indicates parse error
```

unless test-result parsing is configured as required.

Do not incorrectly turn every reporting problem into a failed build.

---

# 81. Checks and infrastructure failures

A required job that cannot execute because:

```text
no runner exists

checkout fails

runner dies
```

must not result in a successful Check.

Use:

```text
Failed
```

or an appropriate non-success conclusion.

Merge remains blocked.

---

# 82. Unit tests

Unit test coverage should include:

```text
pipeline state transitions

job state transitions

step state transitions

runner eligibility

runner heartbeat expiry

required capability matching

required-check evaluation

stale SHA rejection

pipeline result calculation

test result mapping

definition snapshotting

superseded-run logic
```

---

# 83. Integration tests

Integration tests should cover:

```text
Pipelines module enabled

Pipelines module disabled

runner registration

runner heartbeat

manual pipeline execution

Change-created trigger

Change-updated trigger

job assignment

log ingestion

job completion

check publication

Review check consumption

required-check merge gating

cancellation

retry

runner disconnect

artifact metadata persistence
```

Use fake execution where possible for deterministic control-plane tests.

---

# 84. Runner integration tests

Runner tests should execute real local commands such as:

```text
echo

dotnet --version

successful process

failing process

long-running cancellable process
```

Test:

```text
stdout

stderr

exit codes

timeouts

cancellation

workspace cleanup
```

---

# 85. Docker integration smoke test

Provide a non-default smoke test verifying the local runner can access Docker.

Example:

```text
docker run --rm hello-world
```

or equivalent deterministic test.

Do not require Docker for every unit/control-plane test.

---

# 86. Dogfood pipeline

Configure the platform's own repository with a real validation pipeline.

Target:

```text
Validate Platform
```

Jobs:

```text
Build

Unit

Integration

E2E
```

Use actual solution/test project names discovered in the repository.

Do not invent project paths if they differ.

---

# 87. Dogfood acceptance scenario

This is the primary Phase 2 acceptance test.

## Starting state

```text
Phase 1 Review works.

Platform repository is connected to GitHub.

Local runner is registered and online.

Validation pipeline is enabled.
```

## Development

```text
git checkout main

git pull

git checkout -b dogfood/phase-two-pipelines
```

Make a change.

Then:

```text
git add .

git commit -m "Dogfood Phase 2 pipeline"

git push -u origin dogfood/phase-two-pipelines
```

---

# 88. Create Change

From the local platform:

```text
dogfood/phase-two-pipelines
        ↓
main
```

Create/open Change.

Verify:

```text
Change created

Pipeline automatically queued
```

---

# 89. Runner execution

Verify runner:

```text
receives Run

creates isolated workspace

checks out exact HEAD SHA

executes Build

executes Unit

executes Integration

executes E2E
```

The developer's active working tree must not be modified.

---

# 90. Observe execution

Review page should show:

```text
Checks

○ Build
○ Unit Tests
○ Integration Tests
○ E2E Tests
```

As execution proceeds:

```text
✓ Build

✓ Unit Tests

○ Integration Tests

Queued E2E Tests
```

Logs should be viewable while jobs run.

---

# 91. Failure acceptance

Introduce or use a branch containing a deterministic failing test.

Verify:

```text
test fails

job fails

check fails

Review shows failure

merge is blocked

failed test is visible

logs are visible
```

If diagnostic artifact output exists:

```text
artifact is accessible
```

---

# 92. Update Change

Fix the failing test.

Then:

```text
git add .

git commit -m "Fix failing test"

git push
```

Verify:

```text
Change HEAD updates

old checks become stale

old run may be cancelled/superseded

new pipeline starts

new run uses new SHA
```

---

# 93. Pass acceptance

Verify all required checks pass:

```text
✓ Build

✓ Unit Tests

✓ Integration Tests

✓ E2E Tests
```

Review now shows:

```text
Merge Requirements

✓ Required approval

✓ No changes requested

✓ Build

✓ Unit Tests

✓ Integration Tests

✓ E2E Tests

Ready to merge
```

---

# 94. Merge acceptance

Approve Change.

Merge using the platform.

Verify:

```text
GitHub PR merged

Review Change merged

Pipeline history retained

Checks retained

Test history retained
```

Then:

```text
git checkout main

git pull
```

Continue development.

---

# 95. Phase 2 completion criteria

Phase 2 is complete only when:

```text
Pipelines is genuinely modular.

A local runner can register.

Runner health is visible.

A pipeline can be configured.

A pipeline can run manually.

A Change can trigger a pipeline.

The exact Change SHA is checked out.

Execution occurs in an isolated workspace.

The active development checkout is not modified.

Commands execute correctly.

Logs stream live.

Jobs succeed/fail based on exit codes.

Cancellation works.

Build tests can run.

Unit tests can run.

Integration tests can run.

E2E tests can run.

Docker/Testcontainers workloads work locally.

TRX results can be parsed.

Test summaries appear in the platform.

Failed tests are inspectable.

Run artifacts can be retained.

Checks are published.

Checks appear in Review through an extension.

Checks are SHA-specific.

Stale checks cannot satisfy merge policy.

Required failed/pending checks block merge.

New commits trigger fresh validation.

Successful required checks permit merge.

Historical runs remain inspectable.

Pipelines can be disabled without breaking Review.

The platform can review, test, and merge development of itself.
```

## Implementation status (ForgeDeck)

Control plane + local runner are implemented and modular (events + `ICheckProvider`; no Review↔Pipelines project references).

**In place:** runner register/heartbeat/revoke, cancel with ack, isolated workspace + SHA checkout, definition CRUD, manual runs with repository URL, ChangeOpened/Updated/Push triggers, supersede, TRX + artifacts (upload/download), Checks + RequiredChecks merge gate, live run polling UI, `ArtifactReference`, Simulated vs Runner modes (`Development` defaults to Runner).

**Dogfood notes:** start Platform.Api before PipelineRunner; use a fresh registration token; set GitHub credential for private clones. Opt-in smokes: `FORGEDECK_DOCKER_SMOKE=1`, `FORGEDECK_GITHUB_SMOKE=1` (+ `_MUTATE=1` for write path).

---

# 96. Explicit deferral to Phase 3

Do NOT implement during Phase 2:

```text
Deploy Module

Applications

Releases

Environments

Deployment Targets

Deployment Agents

Docker application deployment

Deployment approvals

Promotion

Rollback

Development environment deployment
```

Phase 3 will consume validated outputs from Pipelines.

---

# 97. Phase 3 compatibility requirement

Phase 2 should leave a clean concept for a successful pipeline producing deployable output.

Conceptually:

```text
Pipeline
    |
    v
ArtifactReference
    |
    v
Future Deploy
```

An `ArtifactReference` should be capable of identifying:

```text
local file

OCI image

external registry image

package

deployment bundle
```

Do not build Deploy yet.

Do not tightly bind pipeline output to local filesystem paths.

---

# 98. Intended Phase 3 progression

The future workflow will become:

```text
Local Development
      |
      v
GitHub
      |
      v
Review
      |
      v
Pipelines
      |
      +-- Unit
      +-- Integration
      +-- E2E
      |
      v
Deploy
      |
      v
Local Docker Environment
```

Phase 2 must stop before the Deploy boundary.

---

# 99. Implementation order

Implement in this order:

```text
1. Pipelines module skeleton

2. Pipeline domain model

3. Pipeline definition persistence

4. Manual runs

5. Runner registration

6. Runner authentication

7. Heartbeats

8. Runner work polling/assignment

9. Isolated workspaces

10. Git checkout by SHA

11. Process execution

12. Log streaming

13. Job/step state

14. Cancellation

15. Change event triggers

16. Check model/provider

17. Review Checks extension

18. Required-check merge policy

19. Test result model

20. TRX parser

21. Test UI

22. Run artifacts

23. Docker/Testcontainers verification

24. Superseded-run behaviour

25. Retry

26. Full dogfood pipeline

27. Dogfood Phase 2 using the platform itself
```

Do not begin Deploy until the complete dogfood acceptance scenario passes.

---

# 100. Guiding implementation rule

The implementation agent should optimise for:

> A developer pushes a Change, closes their terminal, opens the platform, and can watch their actual Unit, Integration, and E2E validation execute locally and determine whether that Change is safe to merge.

Do not build a general-purpose replacement for every CI system during Phase 2.

Build the smallest complete Pipelines product that is good enough to validate development of the platform itself.

The critical architecture boundary is:

```text
Review
    |
    | events / checks
    |
Pipelines
    |
    | runner protocol
    |
Local Runner
```

No optional module should become implementation-dependent on another.

The final Phase 2 product must be capable of:

> **reviewing, testing, and merging its own development using its own Review and Pipelines modules.**
