# ForgeDeck — Product & Feature Specification

## 1. Product Objective

ForgeDeck is a modular, self-hosted DevOps platform covering the software delivery lifecycle:

```text
Git → Code → Review → Build → Deploy
```

Each area is an independently enabled product module.

ForgeDeck provides:

* one coherent web interface;
* one Organisation per installation;
* multiple Projects;
* single-repository and multi-repository Projects;
* Community and Enterprise editions;
* offline-compatible licensing;
* project-scoped software delivery workflows;
* organisation-wide governance and administration;
* local/on-premise operation as a first-class deployment model.

The platform should remain useful when only a subset of modules is installed.

Example installations:

```text
Code + Review

Code + Review + Build

Git + Code + Review

Code + Build + Deploy

Git + Code + Review + Build + Deploy
```

No optional module may require all other optional modules to exist.

---

# 2. Product Terminology

## 2.1 Installation

A deployed ForgeDeck server.

For the initial self-hosted product:

```text
1 Installation = 1 Organisation
```

A second Organisation requires a second ForgeDeck installation.

---

## 2.2 Organisation

The top-level administrative boundary.

An Organisation owns:

```text
Users
Groups / Teams
Projects
Integrations
Modules
Licensing
Security Policy
Audit
Global Configuration
```

Example:

```text
Northstar Labs
```

---

## 2.3 Project

A Project represents a software product, system, or engineering workload.

Examples:

```text
Atlas

Stockyards

Internal Tools
```

Projects contain one or more Repositories and provide the primary context for:

```text
Code
Review
Build
Deploy
```

---

## 2.4 Repository

A source-code repository belonging to a Project.

Repositories may be:

```text
Hosted by ForgeDeck Git

GitHub

GitLab

Azure Repos

Other future source providers
```

A Project can operate in:

```text
Single Repository mode

Multi Repository mode
```

---

# 3. Core Hierarchy

```text
FORGEDECK INSTALLATION
        =
    ORGANISATION
         │
         ├── Users
         ├── Groups / Teams
         │
         ├── Projects
         │     │
         │     ├── Repositories
         │     │
         │     ├── Code
         │     ├── Review
         │     ├── Build
         │     └── Deploy
         │
         ├── Integrations
         ├── Extensions
         ├── Modules
         ├── Licensing
         ├── Security
         ├── Audit
         └── Settings
```

---

# 4. Product Modules

The initial first-class modules are:

```text
Git

Code

Review

Build

Deploy
```

These are product/licensing boundaries.

They do not necessarily correspond to separate services or processes.

---

# 5. Git Module

## 5.1 Purpose

Provide native repository hosting.

Git owns repository transport and storage rather than repository browsing UX.

---

## 5.2 Features

Initial Git module capabilities:

```text
Repository creation

Repository storage

Git refs

Branches

Tags

Commits

SSH clone/fetch/push

HTTPS clone/fetch/push

Repository permissions

Repository hooks

Default branch

Repository archival
```

Future:

```text
Git LFS

Repository mirroring

Protected branches

Signed commit enforcement

Repository federation
```

---

## 5.3 External Git

Git is optional.

ForgeDeck Code, Review, and Build must be capable of using externally hosted repositories.

Example:

```text
GitHub
   │
   ▼
ForgeDeck Code
   │
   ├── Review
   └── Build
```

This allows customers to adopt ForgeDeck without migrating repository hosting.

---

# 6. Code Module

## 6.1 Purpose

Provide source-code visibility and navigation independently of where Git is hosted.

Code consumes a source provider abstraction.

---

## 6.2 Features

Code includes:

```text
Files

Branches

Commits

Tags

Git Graph

Diffs

Blame

Repository Search

Repository Activity
```

---

## 6.3 Files

Support:

```text
directory navigation

file viewing

branch/reference selection

line numbers

syntax-aware display

breadcrumbs

copy path

permalink to commit
```

Binary rendering is optional.

---

## 6.4 Branches

Display:

```text
Branch Name

Latest Commit

Author

Last Activity

Default Branch

Associated Pull Request
```

Actions may later include:

```text
Create Branch

Delete Branch

Protect Branch
```

depending on source provider capability.

---

## 6.5 Commits

Display:

```text
SHA

Message

Author

Timestamp

Parents

Changed Files

Diff
```

---

## 6.6 Tags

Display:

```text
Tag

Commit

Created

Tagger

Associated Release where available
```

---

## 6.7 Git Graph

Provide a visual commit graph representing:

```text
branches

merges

tags

commit relationships
```

It should remain usable on repositories with non-linear history.

---

## 6.8 Repository Activity

Use Activity instead of a dedicated Pushes page.

Example:

```text
Rowan pushed 3 commits to feature/pipelines.

Alice created tag v1.4.0.

Bob deleted feature/old-api.

Change #184 was merged into main.
```

---

# 7. Review Module

## 7.1 Purpose

Provide code review independently of source hosting.

The internal domain concept may remain provider-neutral:

```text
Change
```

while the user-facing term may be:

```text
Pull Request
```

where appropriate.

---

## 7.2 Features

Review provides:

```text
Pull Requests

Review Queue

Diff Review

General Comments

Inline Discussions

Reviewers

Approval / Request Changes

Merge Requirements

Merge

Review History
```

---

## 7.3 Pull Request list

Support filters:

```text
Needs my review

Created by me

Participating

Open

Merged

Closed
```

Additional filters:

```text
Repository

Author

Reviewer

Target Branch

Status
```

---

## 7.4 Review Queue

The Review Queue prioritises work relevant to the current User.

Sections:

```text
Needs my review

Changes requested from me

Approved by me

Recently reviewed
```

---

## 7.5 Pull Request detail

Tabs:

```text
Overview

Changes

Discussion

Reviewers
```

Modules may extend this later:

```text
Checks          from Build

Deployments     from Deploy
```

---

## 7.6 Review policy

Community baseline:

```text
Reviewer assignment

Approve

Request Changes

One required approval

Block merge when Changes Requested exists
```

Enterprise may add:

```text
Multiple required approvals

Group approval

CODEOWNERS

Path policies

Conditional policies

Review dismissal policies
```

---

# 8. Build Module

## 8.1 Purpose

Provide continuous integration and general software build automation.

The product module is called:

```text
Build
```

Its primary execution abstraction is:

```text
Pipeline
```

---

# 9. Build Navigation

```text
Build
├── Pipelines
├── Runs
├── Jobs
├── Tests
└── Artifacts
```

Agent configuration may appear either here or under Project Settings depending on scope.

---

# 10. Pipelines

Pipeline definitions include:

```text
Name

Triggers

Jobs

Steps

Variables

Runner Requirements

Timeouts

Artifacts

Test Result Inputs
```

Supported initial triggers:

```text
Manual

Push

Pull Request Opened

Pull Request Updated
```

Future:

```text
Schedule

Tag

Webhook

Release

Deployment
```

---

# 11. Runs

A Pipeline Run represents one execution of a Pipeline against an immutable source revision.

Display:

```text
Pipeline

Commit SHA

Branch

Pull Request

Trigger

Status

Started

Duration

Runner
```

Statuses:

```text
Queued

Running

Succeeded

Failed

Cancelled
```

---

# 12. Jobs

A Pipeline Run contains Jobs.

Example:

```text
Build

Unit Tests

Integration Tests

E2E Tests
```

Jobs contain ordered Steps.

---

# 13. Job view

Display:

```text
Status

Duration

Runner

Exit Code

Steps

Logs

Tests

Artifacts
```

Logs must support:

```text
live streaming

search

wrap

error filtering

expand/collapse

download
```

---

# 14. Tests

Test results should be first-class structured data.

Support:

```text
Test Run

Test Suite

Test Case
```

Result:

```text
Passed

Failed

Skipped
```

Initial parser:

```text
TRX
```

Future:

```text
JUnit XML

NUnit

Jest

Playwright

pytest
```

---

# 15. Artifacts

Build artifacts represent output associated with a Pipeline Run.

Examples:

```text
TRX results

coverage

Playwright trace

screenshots

ZIP output

OCI image reference

deployment bundle
```

Artifacts are not initially a separate top-level module.

---

# 16. Build Runners

Execution occurs outside the control-plane application.

```text
ForgeDeck
    │
    ▼
Runner
    │
    ├── Build
    ├── Unit
    ├── Integration
    └── E2E
```

Runners advertise capabilities such as:

```text
windows

linux

dotnet

node

docker

playwright
```

---

# 17. Deploy Module

## 17.1 Purpose

Provide release and deployment orchestration.

---

# 18. Deploy Navigation

```text
Deploy
├── Releases
├── Environments
└── Deployments
```

Deployment target/agent administration may live under settings.

---

# 19. Releases

A Release represents an immutable deployment candidate.

Example:

```text
Release 1.4.0

Commit
abc123

Pipeline
#481

Artifacts
api@sha256:abc
web@sha256:def
```

---

# 20. Environments

Examples:

```text
Development

Integration

Test

Staging

Production
```

Environment progression is configurable.

Do not enforce a fixed lifecycle.

---

# 21. Deployments

Deployment records include:

```text
Release

Environment

Target

Agent

Status

Started

Completed

Duration

Logs

Health
```

Support:

```text
manual deployment

health verification

deployment history

retry

rollback
```

---

# 22. Feature Flags

Feature Flags are explicitly NOT part of Deploy initially.

They should remain a possible future first-class module:

```text
Feature Flags
```

because flag evaluation, targeting, SDKs, segments, and experimentation create a substantial independent domain.

---

# 23. Editions

ForgeDeck has two user-facing editions:

```text
Community

Enterprise
```

Community must be genuinely useful.

Enterprise adds:

```text
governance

advanced security

advanced policy

scale

enterprise integrations

administration
```

---

# 24. Licensing Model

The user-facing model is:

```text
ForgeDeck Community

ForgeDeck Enterprise
```

Internally, licensing is capability-based.

Do NOT implement feature enforcement as:

```csharp
if (license.Edition == Enterprise)
```

Prefer:

```csharp
capabilities.Has("Review.MultiApproval")
```

---

# 25. Capability Examples

```text
Review.BasicApproval

Review.MultiApproval

Review.TeamApproval

Review.CodeOwners

Build.LocalRunner

Build.RunnerPools

Build.ParallelExecution

Deploy.LocalDocker

Deploy.Approvals

Security.EntraSso

Audit.AdvancedRetention
```

Enterprise grants an appropriate set of capabilities.

---

# 26. Offline Licence Enforcement

ForgeDeck MUST support fully offline licence validation.

Use a digitally signed licence file.

Do not depend on permanent access to a ForgeDeck licensing server.

Conceptual:

```text
forgedeck.lic

Licence Payload
+
Digital Signature
```

ForgeDeck contains the vendor's:

```text
Public Verification Key
```

The commercial licensing system retains the:

```text
Private Signing Key
```

---

# 27. Licence Payload

Conceptually:

```text
LicenceId

CustomerId

Edition

IssuedAt

ExpiresAt

Entitlements

Limits

Instance Binding
```

Example:

```text
Edition
Enterprise

Entitlements

Review.MultiApproval
Review.CodeOwners
Build.RunnerPools
Deploy.Approvals
Security.EntraSso
```

---

# 28. Licence File Installation

Support both:

```text
UI upload
```

and:

```text
filesystem configuration
```

Example mounted path:

```text
/config/forgedeck.lic
```

This permits Docker/on-prem installations to provision licences declaratively.

---

# 29. Licence Startup Behaviour

On startup:

```text
Load Licence
    ↓
Verify Signature
    ↓
Validate Metadata
    ↓
Resolve Capabilities
    ↓
Populate Capability Registry
```

Invalid or expired Enterprise licensing should not corrupt or destroy stored data.

Community functionality should remain accessible where possible.

---

# 30. Licence Settings

Organisation Settings:

```text
License
```

Community example:

```text
ForgeDeck Community

No commercial licence installed.

[ Upload Enterprise Licence ]
```

Enterprise example:

```text
ForgeDeck Enterprise

Customer
Northstar Labs

Status
Active

Expires
21 Sep 2027

[ Replace Licence ]
```

---

# 31. Organisation Home

Top-level Organisation page.

Header:

```text
Northstar Labs                         [ + New Project ]
```

Primary content:

```text
Starred Projects

All Projects

Organisation Metrics

Pull Requests

Build Health

Environment Health
```

Modules contribute content dynamically.

---

# 32. Starred Projects

Users may star Projects.

Example:

```text
Starred Projects

Atlas
2 open reviews
Build passing
Development healthy

Stockyards
4 open reviews
Build failing
Integration healthy
```

Stars are User-specific.

---

# 33. Project Grid

Display Projects in grid or table form.

Suggested information:

```text
Project

Repositories

Open Reviews

Build Status

Environment Status

Last Activity
```

Only installed modules contribute relevant columns.

Example:

```text
Atlas
1 repo
2 reviews
Passing
Healthy

Stockyards
5 repos
4 reviews
Failing
Healthy
```

---

# 34. Home View

ForgeDeck Home is User-oriented rather than purely administrative.

It should be configurable.

Provide a good default rather than requiring setup from scratch.

---

# 35. Default Home Dashboard

Suggested default widgets:

```text
Organisation Overview

My Pull Requests

Build Health

Environment Health

Recent Activity

Starred Projects
```

---

# 36. Organisation Metric Overview

Example:

```text
Projects              8

Repositories          21

Open Pull Requests    12

Failed Builds          2

Active Environments    6
```

Hide metrics belonging to unavailable modules.

---

# 37. Pull Request Home Widget

Filters:

```text
Needs my review

Created by me

Participating

Open
```

Example:

```text
#184  Add pipeline runner
Atlas
Needs your review

#181  Improve GitHub provider
Atlas
Approved

#177  Add SMA stub
Stockyards
Changes requested
```

---

# 38. Build Home Widget

Example:

```text
Atlas
main
Passed
2 minutes ago

Stockyards
main
Failed
17 minutes ago
```

---

# 39. Environment Widget

Example:

```text
Atlas / Development
Healthy
1.4.2

Stockyards / Integration
Healthy
3.8.1

Stockyards / Production
Healthy
3.7.9
```

---

# 40. Dashboard Customisation

Users may later:

```text
add widget

remove widget

move widget

resize widget

configure filters
```

Dashboard configuration is User-specific unless explicitly saved as an Organisation/Project dashboard.

---

# 41. Organisation Settings

Organisation Settings are server-wide because one installation represents one Organisation.

Recommended structure:

```text
GENERAL
├── Overview
├── Projects
├── Users
├── Groups
└── Global Notifications

SECURITY
├── Authentication
├── Policies
├── Permissions
└── Service Accounts

PLATFORM
├── Extensions
├── Modules
├── Usage
├── License
└── Auditing

BUILD
├── Agent Pools
├── Parallel Jobs
├── Defaults
├── Test Retention
└── Artifact Retention

DEPLOY
├── Deployment Agents
├── Target Defaults
└── Release Retention
```

Only relevant module settings appear.

---

# 42. Organisation General — Overview

Display/edit:

```text
Organisation Name

Description

Logo

Installation URL

Time Zone

Default Preferences
```

---

# 43. Organisation Projects

Administrative Project view.

Display:

```text
Project

Repository Mode

Repositories

Members

Created

Last Activity

Status
```

Actions:

```text
Create

Archive

Restore

Open Settings
```

---

# 44. Organisation Users

Members grid:

```text
Account

Role

Expiration

Access Granted

Last Activity
```

Example:

| Account     | Role   | Expiration | Access Granted | Last Activity |
| ----------- | ------ | ---------- | -------------- | ------------- |
| Rowan Smith | Owner  | Never      | 12 Aug         | 2 min ago     |
| Alice Chen  | Member | Never      | 19 Aug         | 3 hours ago   |
| Bob Taylor  | Admin  | 30 Sep     | 20 Aug         | Yesterday     |

---

# 45. User Access Metadata

Track:

```text
User Created

Organisation Access Granted

Access Expiration

Last Authentication

Last Platform Activity

Granted By
```

Do not overload a single vague `Activity` column.

---

# 46. User Expiration

Organisation membership may optionally expire.

Example:

```text
Contractor

Access expires
30 Sep 2026
```

Expired membership:

```text
cannot authenticate/use organisation resources
```

unless another active access rule permits it.

Historical attribution remains.

---

# 47. Groups

Groups organise Users.

Example:

```text
Platform

Backend

Frontend

DevOps

Security
```

Groups may be used by:

```text
Project Permissions

Review Policies

Build Administration

Deployment Approvals
```

---

# 48. Authentication Settings

Authentication providers:

```text
Local

Microsoft Entra ID

OIDC

SAML
```

Community may initially provide:

```text
Local
```

Enterprise may provide:

```text
Microsoft Entra ID

advanced SSO
```

according to capability licensing.

---

# 49. Microsoft Entra ID

Do not make Entra a standalone top-level settings section.

Place it under:

```text
Security
    Authentication
```

Configuration may include:

```text
Tenant ID

Client ID

Client Secret / Certificate

Allowed Domains

Group Mapping

Default Role
```

---

# 50. Security Policies

Organisation-wide policies may include:

```text
Password requirements

Session duration

MFA requirements

API token policies

Access expiration defaults

Repository access policy

Runner registration policy

Deployment agent policy
```

Implement incrementally.

---

# 51. Permissions

Organisation Permissions page should expose:

```text
Users

Groups
```

and effective permission assignments.

Do not initially build an enormous enterprise IAM product.

Start with:

```text
Owner

Admin

Member
```

plus Project roles/capabilities.

---

# 52. Auditing

Organisation-wide Audit page.

Audit records include:

```text
Timestamp

Actor

Action

Resource

Project

Module

Result

Metadata

Correlation ID
```

Filters:

```text
User

Project

Module

Action

Date

Result
```

---

# 53. Global Notifications

Organisation default notification policy.

Examples:

```text
Email enabled

Build failures

Pull Request assignments

Deployment failures

Security events
```

Users may override certain defaults.

---

# 54. Usage

Usage page may display:

```text
Users

Projects

Repositories

Storage

Build Minutes

Parallel Jobs

Artifacts

Deployment Targets
```

Enterprise limits may later use this data.

Do not make Usage dependent on cloud telemetry.

---

# 55. Extensions

Extensions represent external/in-process additions to ForgeDeck.

Examples:

```text
GitHub

GitLab

Jira

Slack

Custom provider
```

Display:

```text
Installed

Enabled

Version

Status

Configuration
```

---

# 56. Modules Settings

Display first-class ForgeDeck modules:

```text
Git

Code

Review

Build

Deploy
```

Example:

```text
Code
Enabled
Community

Review
Enabled
Enterprise

Build
Enabled
Enterprise

Deploy
Disabled
Enterprise entitlement available
```

---

# 57. Module Enablement

Installed, Enabled, and Licensed are separate states.

Example:

```text
Module:
Build

Installed
Yes

Enabled
Yes

Edition
Enterprise
```

A module may be:

```text
installed but disabled

licensed but not installed
```

depending on packaging strategy.

---

# 58. Build Organisation Settings

Organisation-wide Build settings include:

```text
Agent Pools

Parallel Jobs

Defaults

Test Retention

Artifact Retention
```

---

# 59. Agent Pools

Organisation defines Runner pools:

```text
Windows

Linux

Docker

High Memory
```

Projects may later restrict which pools they can use.

---

# 60. Parallel Jobs

Display:

```text
Configured Parallelism

Current Usage

Queued Jobs
```

Enterprise may unlock higher/advanced parallelism depending on commercial model.

---

# 61. Test Retention

Organisation default:

```text
Test Results
30 days
```

Projects may override where permitted.

---

# 62. Artifact Retention

Organisation default:

```text
Artifacts
30 days
```

Policies may later support:

```text
keep latest N

keep release artifacts

keep failed-run artifacts longer
```

---

# 63. Deploy Organisation Settings

Organisation-level defaults:

```text
Deployment Agents

Target Defaults

Release Retention

Deployment Retention
```

---

# 64. Project Settings

Recommended structure:

```text
GENERAL
├── Overview
├── Members & Teams
├── Permissions
├── Notifications
├── Service Hooks
└── Dashboards

REPOSITORIES
└── Repositories

REVIEW
└── Policies

BUILD
├── Agent Pools
├── Parallelism
├── Settings
├── Test Management
└── Artifact Retention

DEPLOY
├── Release Retention
└── Settings
```

Sections disappear when modules are unavailable.

---

# 65. Project Overview Settings

Fields:

```text
Name

Slug

Description

Visibility

Repository Mode

Project Status
```

---

# 66. Repository Modes

A Project supports:

```text
Single Repository

Multi Repository
```

Internally both use:

```text
Project
    ↓
Repositories[]
```

Repository Mode primarily controls UX and validation.

---

# 67. Single Repository Mode

Rules:

```text
maximum one active Repository
```

UI simplifies repository context.

Example:

```text
Atlas

Code
Files
Branches
Commits
```

rather than constantly displaying:

```text
Repository: atlas
```

---

# 68. Multi Repository Mode

Allows multiple Repositories.

Example:

```text
Stockyards

Repositories

stockyards-api
stockyards-web
sma-control
infrastructure
```

Repository-aware views expose selector/filter.

---

# 69. Repository Mode Conversion

Allow:

```text
Single → Multi
```

without data migration beyond mode change.

Allow:

```text
Multi → Single
```

only when:

```text
active repositories <= 1
```

---

# 70. Project Members

Project Settings → Members & Teams.

Grid:

```text
Account

Role

Expiration

Access Granted

Last Activity
```

Example:

```text
Rowan Smith
Project Admin
Never
12 Aug
2 min ago

Alice Chen
Developer
Never
19 Aug
3 hours ago

Bob Taylor
Reviewer
30 Sep
20 Aug
Yesterday
```

---

# 71. Project Roles

Initial roles may include:

```text
Project Admin

Developer

Reviewer

Viewer
```

Do not introduce a complex custom-role designer initially.

Permissions ultimately remain capability-based.

---

# 72. Project Member Expiration

Project access may have independent expiration.

Example:

```text
Organisation account remains active

Project access expires 30 Sep 2026
```

After expiration:

```text
User remains Organisation Member

Project becomes inaccessible
```

Historical actions remain attributable.

---

# 73. Teams in Project Settings

Projects can grant access to:

```text
Users

Groups
```

Example:

```text
Groups

Platform
Developer

Security
Reviewer
```

This avoids individually adding every User.

---

# 74. Project Permissions

Project permission categories may include:

```text
Project

Repositories

Review

Build

Deploy
```

Actual options depend on installed modules.

---

# 75. Project Notifications

Configure defaults such as:

```text
Pull Request changes

Build failures

Deployment failures

Environment changes
```

User-level preferences may override optional notifications.

---

# 76. Service Hooks

Project-level outgoing hooks.

Examples:

```text
Webhook

Slack

Teams

Jira
```

Events:

```text
Pull Request opened

Build completed

Release created

Deployment completed
```

---

# 77. Dashboards

Projects support one or more dashboards.

Default:

```text
Overview
```

Future:

```text
Team Dashboard

Release Dashboard

Operations Dashboard
```

Widgets are module-contributed.

---

# 78. Repository Settings

Project Settings → Repositories.

Display:

```text
Repository

Provider

Default Branch

Status

Local Workspace where relevant
```

Actions:

```text
Connect

Disconnect

Archive

Configure
```

---

# 79. Review Project Settings

Review contributes:

```text
Review
    Policies
```

Community:

```text
Require one approval

Allow self-review
```

Enterprise:

```text
Approval count

Group approval

CODEOWNERS

Path policies

Discussion resolution policy
```

---

# 80. Build Project Settings

Build contributes:

```text
Agent Pools

Parallel Jobs

Settings

Test Management

Artifact Retention
```

Project values inherit Organisation defaults unless overridden.

---

# 81. Settings Inheritance

Use:

```text
Organisation Default
        ↓
Project Override
```

Example:

```text
Organisation Test Retention
30 days

Project Atlas
90 days
```

Clearly show when a setting is inherited.

Example:

```text
Test retention

30 days
Inherited from Organisation

[ Override ]
```

---

# 82. Project View

Main Project navigation:

```text
Overview

Code

Review

Build

Deploy

Settings
```

Modules appear only when enabled.

---

# 83. Project Overview

Project Overview should be configurable but provide useful defaults.

Suggested widgets:

```text
Repository Activity

Pull Requests

Build Health

Recent Runs

Environment Status

Recent Deployments
```

---

# 84. Project Overview Summary

Example:

```text
Atlas

Repositories
1

Open Pull Requests
3

Build
Passing

Development
Healthy
```

---

# 85. Code Navigation

```text
Code
├── Files
├── Branches
├── Commits
├── Tags
└── Graph
```

Optional future:

```text
Search

Activity
```

Do not create a dedicated Pushes page initially.

---

# 86. Review Navigation

```text
Review
├── Pull Requests
└── Review Queue
```

Potential future:

```text
Policies
```

should remain under Project Settings rather than primary navigation.

---

# 87. Build Navigation

```text
Build
├── Pipelines
├── Runs
├── Jobs
├── Tests
└── Artifacts
```

Avoid duplicating `Pipelines` and `Runs` under an additional `Automation` product label if Build is the selected product vocabulary.

---

# 88. Deploy Navigation

```text
Deploy
├── Releases
├── Environments
└── Deployments
```

Future functionality may add:

```text
Deployment Processes
```

if needed.

---

# 89. Single Repository Project UX

For Single Repository Projects:

```text
Code
Review
Build
```

implicitly operate against the sole Repository where possible.

Example:

```text
Review
    Pull Requests
```

not:

```text
Repository
atlas

Pull Requests
```

unless repository identity materially matters.

---

# 90. Multi Repository Project UX

For Multi Repository Projects:

Code requires repository context:

```text
Repository
[ stockyards-api ▼ ]
```

Review defaults to aggregate:

```text
Repository
[ All Repositories ▼ ]
```

Build pipelines specify source repository where applicable.

---

# 91. Starred Projects

Users may star/unstar Projects.

Persist per User.

Starred Projects appear prominently on Home and Organisation Overview.

---

# 92. Search / Command Navigation

Future global navigation should support:

```text
Projects

Repositories

Pull Requests

Pipeline Runs

Releases

Users
```

A command/search bar may use:

```text
Ctrl/Cmd + K
```

Do not require full global search in the first implementation.

---

# 93. First-Run Onboarding

Fresh installation flow:

```text
Bootstrap Login
      ↓
Organisation Name
      ↓
Licence
      ↓
Owner Account
      ↓
First Project
      ↓
Repository Mode
      ↓
Repository Connection
      ↓
Invite Members
      ↓
Module Setup
      ↓
Ready
```

---

# 94. Bootstrap Login

Development:

```text
admin / admin
```

may be supported.

Production should prefer generated/environment-provided bootstrap credentials.

Bootstrap identity exists only to establish the first real Owner.

---

# 95. Organisation Setup

Form:

```text
Organisation Name

Description

Logo
```

Example:

```text
Northstar Labs
```

Only one Organisation can exist.

---

# 96. Licence Setup

Immediately after Organisation setup:

```text
Choose Edition

Community

Enterprise
```

Community:

```text
[ Continue with Community ]
```

Enterprise:

```text
[ Upload Licence ]
```

Enterprise licence is locally verified.

No network access is required.

---

# 97. Owner Setup

Create:

```text
Display Name

Username

Email

Password
```

First real User becomes:

```text
Owner
```

After successful creation:

```text
bootstrap account disabled
```

---

# 98. First Project Setup

Fields:

```text
Name

Description

Visibility

Repository Mode
```

Repository Mode:

```text
Single Repository

Multiple Repositories
```

---

# 99. Repository Setup

Sources:

```text
Local Repository + GitHub

GitHub

Future external provider

Future ForgeDeck Git
```

Local workflow may detect `.git` and configured remotes.

---

# 100. Member Setup

Optional onboarding step:

```text
Invite Users

Assign Role

Optionally Assign Project Access
```

May be skipped.

---

# 101. Module Setup

Installed modules contribute onboarding.

Review:

```text
Basic Review Policy
```

Build:

```text
Create Pipeline
Register Runner
```

Deploy:

```text
Create Development Environment
Register Agent
```

Capabilities shown depend on licence.

---

# 102. Finish Onboarding

Summary:

```text
Northstar Labs

ForgeDeck Enterprise

Owner
Rowan Smith

Project
Atlas

Repository Mode
Single

Repository
northstar/atlas

Modules
Code
Review
Build
```

Then:

```text
[ Open Atlas ]
```

---

# 103. Onboarding Resume

Setup must survive interruption.

Derive completion from persisted entities where possible.

Example:

```text
Organisation ✓

Licence ✓

Owner ✓

Project incomplete
```

After login, resume at Project.

---

# 104. Architecture

The control plane should initially be a modular monolith.

Conceptual:

```text
ForgeDeck Server

Core
Git
Code
Review
Build
Deploy
```

Separate execution components:

```text
Build Runner

Deployment Agent
```

Potential future:

```text
Git transport/storage service
```

when justified.

---

# 105. Module Boundaries

Modules must communicate through:

```text
Contracts

Events

Provider Interfaces

Extension Points

Stable IDs
```

They must not:

```text
query another module's tables

reference another module's infrastructure layer

depend on another optional module implementation
```

---

# 106. Persistence

One database is acceptable initially.

Logical ownership:

```text
core.*

git.*

code.*

review.*

build.*

deploy.*
```

Code may not require substantial persistence if mostly provider-backed.

Each module owns its migrations/schema.

---

# 107. UI Extension Model

Modules may contribute:

```text
Navigation

Routes

Dashboard Widgets

Settings Sections

Resource Tabs

Actions

Onboarding Steps
```

Example:

```text
Review Pull Request

Overview
Changes
Discussion
Reviewers
[Build → Checks]
[Deploy → Deployments]
```

---

# 108. Module Disabled Behaviour

When a module is disabled:

```text
navigation disappears

routes disappear

API disappears

background handlers disappear

settings disappear

onboarding contributions disappear
```

Other modules continue operating.

---

# 109. Core Roles

Initial Organisation roles:

```text
Owner

Admin

Member
```

Project roles:

```text
Project Admin

Developer

Reviewer

Viewer
```

Fine-grained permission implementation may evolve behind these defaults.

---

# 110. Owner Protection

Organisation must always have at least one active Owner.

Prevent:

```text
demoting final Owner

removing final Owner

suspending final Owner

expiring final Owner access
```

---

# 111. Audit Requirements

Audit:

```text
Authentication

User Management

Permission Changes

Project Changes

Repository Changes

Module Changes

Licence Changes

Review Actions

Build Actions

Deployment Actions
```

Audit must be append-oriented.

---

# 112. Security

Server-side authorization is mandatory.

Every resource operation evaluates:

```text
Authenticated?

Active User?

Organisation Role?

Project Access?

Module Permission?

Capability Licensed?

Resource Accessible?
```

Frontend visibility does not replace authorization.

---

# 113. Core Completion Criteria

The platform foundation is complete when:

```text
A fresh server can be onboarded.

One Organisation is created.

Community or Enterprise licensing can be selected.

Enterprise licence works offline.

The first permanent User becomes Owner.

Projects can be created.

Projects support Single and Multi Repository modes.

Users and Groups can be managed.

Project access can expire.

Projects can contain repositories.

Project and Organisation settings are separated.

Organisation defaults can be inherited by Projects.

Modules contribute navigation and settings dynamically.

Home and Project dashboards consume module widgets.

Community and Enterprise capabilities are enforced server-side.
```

---

# 114. Product Module Completion Criteria

The architecture must support these valid installations:

```text
Code

Code + Review

Code + Review + Build

Git + Code + Review

Code + Build + Deploy

Git + Code + Review + Build + Deploy
```

No optional module may assume every other module exists.

---

# 115. Product Navigation Summary

## Organisation

```text
Home

Projects

People

Settings
```

## Project

```text
Overview

Code

Review

Build

Deploy

Settings
```

## Organisation Settings

```text
General

Projects

Users & Groups

Security

Authentication

Permissions

Global Notifications

Extensions

Modules

Usage

License

Auditing

Build Settings

Deploy Settings
```

## Project Settings

```text
General

Members & Teams

Permissions

Notifications

Service Hooks

Dashboards

Repositories

Review Policies

Build Settings

Test Management

Artifact Retention

Deploy Settings

Release Retention
```

Sections are dynamic based on installed modules.

---

# 116. Product Vocabulary

The main customer-facing lifecycle is:

```text
Git.
Code.
Review.
Build.
Deploy.
```

Definitions:

```text
Git
Host the source.

Code
Understand the source.

Review
Approve the change.

Build
Validate and package the change.

Deploy
Release the change.
```

This vocabulary should remain consistent throughout:

```text
Navigation

Documentation

Licensing

Marketing

Module manifests

Settings
```

---

# 117. Final Product Principle

ForgeDeck should feel like one DevOps platform rather than a collection of tools.

Projects are the connective tissue.

Modules add capability without changing the core mental model:

```text
Organisation
      ↓
Project
      ↓
Software Delivery
```

A small team should be able to run:

```text
Code + Review
```

while a larger organisation can run:

```text
Git + Code + Review + Build + Deploy
```

inside the same product architecture.

The core product promise is:

> **Install only the DevOps capabilities you need, use them through one coherent interface, and retain control of the entire software delivery lifecycle on infrastructure you own.**
