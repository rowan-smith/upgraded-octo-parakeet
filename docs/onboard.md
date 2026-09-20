# Core Foundation — Organisation, Users, Projects, Repositories & First-Run Onboarding

## 1. Objective

Implement the foundational identity, ownership, project, repository, permissions, and onboarding model for the platform.

The core deployment model is:

> **One platform installation represents exactly one Organisation.**

If a customer wants another completely separate Organisation, they deploy another platform instance.

Example:

```text
Server / Installation A
└── Northstar Engineering

Server / Installation B
└── Northstar Research
```

There is no multi-organisation tenancy inside one platform instance.

The primary hierarchy is:

```text
Platform Installation
        =
   Organisation

        │
        ├── Users
        │    └── Profiles
        │
        ├── Teams
        │
        ├── Projects
        │     ├── Repositories
        │     ├── Review
        │     ├── Pipelines
        │     └── Deploy
        │
        ├── Integrations
        │
        ├── Modules
        │
        ├── Audit
        │
        └── Settings
```

This instance boundary is also the primary:

```text
security boundary
data boundary
licensing boundary
configuration boundary
```

---

# 2. Core principles

The foundation must follow these principles:

1. One installation represents one Organisation.
2. Users belong to that Organisation.
3. The first User becomes the Organisation Owner.
4. Organisations contain Projects.
5. Projects contain Repositories.
6. Projects are the primary working context.
7. Repositories remain provider-independent.
8. Modules attach capabilities to Projects.
9. Teams group Users for access and policy.
10. Historical engineering records survive membership changes.
11. Core owns identity and organisational structure.
12. Optional modules must not leak business logic into Core.

---

# 3. Core domain concepts

Core initially owns:

```text
Organisation

User
UserProfile
UserCredential
UserSession

OrganisationMembership

Team
TeamMembership

Project
ProjectUserAccess
ProjectTeamAccess

Repository
RepositoryConnection
UserRepositoryWorkspace

Integration

Invitation

AuditEvent

InstanceConfiguration
```

Optional modules own their own domains:

```text
Review
Pipelines
Deploy
Git
```

---

# 4. Organisation model

Each installation contains exactly one Organisation.

Conceptually:

```text
Organisation

Id
Name
Description
Avatar
CreatedAt
UpdatedAt
```

Example:

```text
Northstar Engineering
```

The Organisation does not require a globally unique slug because there are no sibling Organisations in the same installation.

A short identifier may still exist for:

```text
API output
configuration
display
future federation
```

but URLs do not need to contain an Organisation slug.

---

# 5. Single-Organisation invariant

The database MUST enforce the logical invariant:

```text
Organisation count <= 1
```

After first-run setup:

```text
Organisation count = 1
```

Normal application APIs must not permit creating a second Organisation.

Creating another Organisation means deploying another instance.

---

# 6. Instance and Organisation relationship

For the initial product:

```text
Platform Instance == Organisation
```

Do not create separate concepts for:

```text
Tenant
Workspace
Account
Organisation Container
```

unless a later requirement demonstrates a need.

The Organisation is the root business object of the installation.

---

# 7. User model

A User represents an authenticated human account for this installation.

Users do not exist globally across multiple installations.

Conceptually:

```text
User

Id

Email

Username

Status

CreatedAt

UpdatedAt

LastLoginAt
```

A User may participate in:

```text
Projects
Teams
Reviews
Pipelines
Deployments
```

inside this Organisation.

---

# 8. User profile

Each User has a profile.

Initial fields:

```text
Display Name

Username

Email

Avatar

Bio
```

Optional later:

```text
Job Title

Timezone

Locale

Location

Website

Pronouns

SSH Keys

Signing Keys
```

Profile and authentication identity should remain logically distinct.

---

# 9. Username

Username requirements:

```text
unique within installation

case-insensitive uniqueness

URL safe

human readable
```

Example:

```text
rowan
```

Profile route may be:

```text
/users/rowan
```

or:

```text
/@rowan
```

Do not expose email addresses in profile URLs.

---

# 10. User status

User status:

```text
Active

Suspended

Disabled
```

Prefer disabling over deleting Users.

Historical records such as:

```text
reviews
comments
pipeline actions
deployments
audit records
```

must continue referring to the original User.

---

# 11. Organisation membership

Even though there is only one Organisation, retain an explicit membership object.

Conceptually:

```text
OrganisationMembership

Id
UserId
Role
Status
JoinedAt
InvitedByUserId
```

This keeps:

```text
identity
```

separate from:

```text
organisation role
```

and makes future role expansion cleaner.

---

# 12. Membership roles

Initial roles:

```text
Owner

Admin

Member
```

Avoid creating many built-in roles initially.

Actual authorization should remain permission/capability based.

Roles are convenient default permission bundles.

---

# 13. Owner

Owner is the highest Organisation role.

The first User created during setup automatically becomes:

```text
Owner
```

Owner capabilities include:

```text
manage users

manage roles

manage teams

manage projects

manage repository connections

manage integrations

manage modules

manage licensing

manage instance settings

view audit

perform destructive organisation-level operations
```

---

# 14. Multiple Owners

Support multiple Owners.

Do not model:

```text
Organisation.OwnerUserId
```

Instead:

```text
OrganisationMembership.Role = Owner
```

Example:

```text
Rowan    Owner
Alice    Owner
Bob      Member
```

---

# 15. Last Owner protection

The Organisation MUST always have at least one active Owner.

Prevent:

```text
demoting the final Owner

removing the final Owner

suspending the final Owner

final Owner leaving
```

Example:

```text
Cannot remove this Owner.

The organisation must have at least one active Owner.
```

---

# 16. Admin

Admin may normally:

```text
manage users

manage teams

create/manage projects

manage repository connections

manage integrations

manage ordinary organisation settings
```

Admin should not automatically have:

```text
ownership-transfer rights

organisation reset/delete rights

licensing-owner rights
```

unless explicitly granted.

---

# 17. Member

Member is the ordinary User role.

Membership in the Organisation does not necessarily imply access to every private Project.

Project access may derive from:

```text
Project visibility

direct User access

Team access

Owner/Admin override
```

---

# 18. Teams

Organisation may create Teams.

Examples:

```text
Backend

Frontend

Platform

DevOps

Security
```

Conceptually:

```text
Team

Id
Name
Slug
Description
CreatedAt
UpdatedAt
```

---

# 19. Team membership

A User may belong to multiple Teams.

Example:

```text
Rowan
├── Platform
├── Backend
└── DevOps
```

Conceptually:

```text
TeamMembership

TeamId
UserId
CreatedAt
```

Only active Organisation Users may belong to Teams.

---

# 20. Team purpose

Teams provide reusable identity groups for:

```text
Project access

Review rules

CODEOWNERS-style policies

Pipeline administration

Deployment approval

future notification routing
```

Initial implementation only requires:

```text
create team

edit team

add User

remove User

delete team
```

---

# 21. Project

A Project is the primary working context.

The Organisation may contain many Projects:

```text
Northstar Engineering

Projects
├── Platform
├── Stockyards
├── Internal Tools
└── Website
```

A Project groups related software-delivery concerns.

---

# 22. Project model

Conceptually:

```text
Project

Id

Name

Slug

Description

Visibility

CreatedByUserId

CreatedAt

UpdatedAt

ArchivedAt
```

Example:

```text
Name
Platform

Slug
platform
```

Route:

```text
/projects/platform
```

---

# 23. Project visibility

Initial values:

```text
Private

Organisation
```

## Private

Accessible to:

```text
Owners

Admins

explicitly granted Users

explicitly granted Teams
```

## Organisation

Accessible to all active Organisation Members according to their module permissions.

Future:

```text
Public
```

may be added if needed for public/open-source hosting.

---

# 24. Project access

A Project may grant access directly to:

```text
Users
```

or:

```text
Teams
```

Conceptually:

```text
ProjectUserAccess

ProjectId
UserId
Role/PermissionSet
```

and:

```text
ProjectTeamAccess

ProjectId
TeamId
Role/PermissionSet
```

Avoid duplicating User or Team records inside Project.

---

# 25. Owner and Admin Project access

Owner always retains recovery/admin access to Projects.

Admins may have organisation-wide Project administration according to their role permissions.

Do not permit Project configuration to permanently lock the final Owner out.

---

# 26. Project module ownership

Modules attach capabilities to Projects.

Conceptually:

```text
Project
├── Repositories
├── Review
├── Pipelines
└── Deploy
```

Review owns:

```text
Changes
Reviews
Comments
```

Pipelines owns:

```text
Pipeline definitions
Runs
Runners
Checks
```

Deploy owns:

```text
Applications
Releases
Environments
Deployments
```

Core must not own these module-specific entities.

---

# 27. Project dashboard

Base Core dashboard may contain:

```text
Project Name

Description

Repositories

Members / Teams

Recent Activity
```

Modules contribute additional cards.

Review:

```text
Open Changes
Waiting for Review
```

Pipelines:

```text
Latest Run
Build Health
```

Deploy:

```text
Development Environment
Current Release
```

Core must provide dashboard extension points.

---

# 28. Repository

A Repository represents a source repository associated with a Project.

A Project may contain:

```text
one repository
```

or:

```text
many repositories
```

Example:

```text
Project: Stockyards

Repositories
├── stockyards-api
├── stockyards-web
├── sma-control
└── infrastructure
```

Do not assume:

```text
Project == Repository
```

---

# 29. Repository model

Core stores provider-neutral repository identity.

Conceptually:

```text
Repository

Id

ProjectId

Name

Slug

DefaultBranch

Status

CreatedAt

UpdatedAt

ArchivedAt
```

Repository belongs to exactly one Project.

---

# 30. Repository connection

Repository storage/location is represented separately.

Conceptually:

```text
RepositoryConnection

Id

RepositoryId

ProviderType

ExternalRepositoryId

ExternalOwner

ExternalName

CloneUrl

WebUrl

LastSyncedAt

Status
```

For Phase 1:

```text
ProviderType = GitHub
```

Future:

```text
GitLab
AzureRepos
Bitbucket
NativeGit
```

---

# 31. Repository provider independence

Core must not use:

```text
GitHubRepository

GitHubPullRequest

Octokit DTOs
```

as domain objects.

Core understands:

```text
Repository

RepositoryConnection

Provider ID
```

Provider-specific implementation belongs behind connectors/providers.

---

# 32. Repository status

Initial states:

```text
Connected

Unavailable

AuthenticationRequired

Disconnected
```

Example:

```text
GitHub token revoked
```

results in:

```text
AuthenticationRequired
```

Do not delete the Repository.

---

# 33. Repository disconnect

Disconnecting a Repository should:

```text
stop provider synchronisation

prevent new provider operations

retain historical Review records

retain historical Pipeline records

retain historical Deployment provenance
```

Repository may remain:

```text
Disconnected
```

until reconnected or archived.

---

# 34. Local repository workspace

The platform may associate a local checkout with a User.

Do not make local path an intrinsic Repository property.

Preferred model:

```text
UserRepositoryWorkspace

UserId

RepositoryId

LocalPath

LastDetectedBranch

LastDetectedHead

UpdatedAt
```

Example:

```text
User
Rowan

Repository
platform

Local Path
C:\Development\Platform
```

This correctly allows different Users to have different local paths.

---

# 35. Local workspace usage

Local workspace may provide:

```text
current branch

HEAD SHA

working-tree status

remote discovery

open folder

future editor integration
```

The local checkout is not authoritative Review state.

Uncommitted changes are not part of a Change.

---

# 36. Repository page

Example:

```text
Repository

platform

Provider
GitHub

Default Branch
main

Connection
github.com/example/platform

Your Local Workspace
C:\Development\Platform
```

Possible tabs:

```text
Files

Commits

Branches

Changes
```

`Changes` may be contributed by Review.

---

# 37. Repository selector

If Project has multiple Repositories:

```text
Repository ▼

stockyards-api
stockyards-web
sma-control
```

Repository-specific pages should clearly indicate current context.

---

# 38. Review aggregation

Review operates primarily at Project level.

Example:

```text
Project: Stockyards

Review
```

may aggregate:

```text
#181 stockyards-api
#182 stockyards-web
#183 sma-control
```

Provide filter:

```text
Repository
```

A Change still references exactly one Repository initially.

---

# 39. Pipeline repository relationship

Pipeline definitions belong to Projects.

A pipeline definition may target:

```text
one Repository

multiple Repositories later

no Repository for manual automation later
```

Phase 2 may initially require one source Repository.

Core must not bake Pipelines concepts into Repository.

---

# 40. Deploy relationship

Deploy Applications belong to Projects.

An Application may eventually consume artifacts originating from:

```text
one Repository

multiple Repositories
```

This is why Project remains above Repository and modules.

---

# 41. Integrations

The Organisation may configure shared integrations.

Examples:

```text
GitHub

Jira

Slack

Vault
```

Conceptually:

```text
Integration

Id

ProviderType

Name

ConfigurationReference

Status

CreatedAt
UpdatedAt
```

Secrets must be stored separately/encrypted.

---

# 42. Integration scope

Initial integrations may be Organisation-wide.

Projects may reference an Integration.

Example:

```text
Organisation GitHub Connection
        │
        ├── Project A / Repo A
        └── Project B / Repo B
```

Avoid requiring duplicate GitHub credentials per Project.

---

# 43. Users and authentication

Initial authentication:

```text
Email

Password
```

Architecture must permit future:

```text
OIDC

SAML

LDAP

SCIM
```

Authentication belongs to Core.

---

# 44. Password security

Passwords:

```text
must never be stored plaintext

must never be logged

must use established framework password hashing
```

Do not implement custom cryptography.

---

# 45. Sessions

Support:

```text
login

logout

session expiration

session revocation
```

Future User Security page may expose:

```text
active sessions
```

---

# 46. API tokens

Users may later create personal API tokens.

Initial conceptual scope:

```text
Token

UserId

Name

Scopes

ExpiresAt

LastUsedAt

RevokedAt
```

Tokens must be stored securely.

Do not store raw token after creation where avoidable.

---

# 47. Invitations

Owner/Admin may invite another User.

Initial invitation fields:

```text
Email

Role
```

Example:

```text
Email
alice@example.com

Role
Member
```

If email delivery is unavailable, generate:

```text
copyable invitation link
```

---

# 48. Invitation model

Conceptually:

```text
Invitation

Id

Email

Role

TokenHash

InvitedByUserId

ExpiresAt

AcceptedAt

CreatedAt
```

Invitation token must:

```text
be random

expire

be single-use

not be stored plaintext where practical
```

---

# 49. Existing User invitation

If account already exists:

```text
Invitation
    ↓
Sign in
    ↓
Accept
```

Since there is only one Organisation per installation, accepting activates/creates the Organisation membership.

---

# 50. New User invitation

Flow:

```text
Open Invitation
    ↓
Create User
    ↓
Complete Profile
    ↓
Accept Membership
    ↓
Open Organisation
```

Do not ask invited Users to create an Organisation.

---

# 51. First-run state

Fresh installation begins:

```text
InstanceState = Uninitialised
```

There are:

```text
0 Organisations

0 Users
```

Normal application access should redirect to:

```text
/setup
```

---

# 52. First-run security

Bootstrap is security-sensitive.

Setup may only execute if:

```text
InstanceState = Uninitialised
```

After successful setup:

```text
InstanceState = Initialised
```

and bootstrap endpoints become unavailable.

Do not rely only on frontend hiding.

---

# 53. Bootstrap concurrency

Two requests must not both bootstrap the server.

Use:

```text
database transaction

locking

unique constraint

instance-state compare-and-set
```

or equivalent.

The database must guarantee only one successful bootstrap.

---

# 54. First-run onboarding flow

Recommended:

```text
Welcome
   ↓
Organisation
   ↓
Owner Account
   ↓
First Project
   ↓
Connect Repository
   ↓
Configure Enabled Modules
   ↓
Ready
```

Organisation and Owner may be presented on the same screen if UX is cleaner.

---

# 55. Step 1 — Welcome

Example:

```text
Welcome

Set up your software delivery platform.

This installation represents one organisation.

[ Get Started ]
```

Do not introduce:

```text
tenancy
module licensing
deployment architecture
```

on the welcome screen.

---

# 56. Step 2 — Organisation

Form:

```text
Organisation

Name
Northstar Engineering

Description
Optional

[ Continue ]
```

Optional avatar may be configured later.

---

# 57. Step 3 — Owner account

Form:

```text
Create Owner Account

Display Name
Rowan Smith

Username
rowan

Email
rowan@example.com

Password
********

Confirm Password
********

[ Continue ]
```

The first User automatically receives:

```text
Organisation Membership

Role = Owner
```

---

# 58. Atomic initialisation

Organisation and first Owner creation must be transactional.

Successful setup produces:

```text
Organisation

User

UserProfile

OrganisationMembership
    Role = Owner

InstanceState
    Initialised
```

Never leave:

```text
Organisation without Owner
```

or:

```text
Owner without Organisation
```

because setup partially failed.

---

# 59. No separate Platform Administrator

For the initial self-hosted product:

```text
Organisation Owner
```

is the highest administrative role.

Do not introduce a separate:

```text
Platform Administrator
```

concept.

Because:

```text
Instance == Organisation
```

the distinction provides little value.

A hosted/SaaS control plane may introduce operator roles later outside this installation model.

---

# 60. Step 4 — First Project

Form:

```text
Create your first project

Name
Platform

Slug
platform

Description
Modular software delivery platform

Visibility
Private

[ Create Project ]
```

Allow:

```text
Skip for now
```

if required.

The guided dogfood path should encourage creation.

---

# 61. Step 5 — Connect Repository

If source/review capability is available:

```text
Connect your code

○ Local repository + GitHub

○ GitHub repository

○ Skip
```

For local dogfooding, prioritise:

```text
Local repository + GitHub
```

---

# 62. Detect local repository

Input:

```text
Local Repository Path

C:\Development\Platform
```

Detect:

```text
Git repository

current branch

HEAD

remotes

origin URL
```

Example:

```text
Git Repository Detected

Branch
main

Remote
origin

GitHub Repository
example/platform
```

---

# 63. GitHub connection

If GitHub integration does not exist:

```text
Connect GitHub

Personal Access Token
********************

[ Validate ]
```

Validation:

```text
credential valid

repository accessible

required API permissions available
```

Never log the token.

---

# 64. Repository creation during onboarding

Create:

```text
Repository

Name
platform

Project
Platform

Default Branch
main
```

and:

```text
RepositoryConnection

Provider
GitHub

External Repository
example/platform
```

and optionally:

```text
UserRepositoryWorkspace

User
Rowan

LocalPath
C:\Development\Platform
```

---

# 65. Module-aware onboarding

Core owns only generic onboarding.

Modules may contribute setup steps.

Examples:

## Review

```text
Configure Review

Require one approval

Allow self-review
```

## Pipelines

```text
Create validation pipeline

Register local runner
```

## Deploy

```text
Create Development environment

Register deployment agent
```

---

# 66. Onboarding extension contract

Conceptually support:

```text
IOnboardingStepContributor
```

Contribution metadata:

```text
Id

Title

Order

Required

Completion check

Route/component
```

Core coordinates onboarding.

Core must not hard-code detailed Review/Pipelines/Deploy configuration.

---

# 67. Review onboarding

If Review is enabled:

```text
Review Setup

☑ Require one approval

☑ Allow self-review
```

For initial single-developer dogfooding:

```text
Allow self-review
```

may default true.

Clearly indicate this is useful for local development.

---

# 68. Pipelines onboarding

When Phase 2 exists, Pipelines may contribute:

```text
Set up local validation

[ Register Local Runner ]

[ Create Pipeline ]
```

This must not appear when Pipelines module is disabled.

---

# 69. Deploy onboarding

When Phase 3 exists, Deploy may contribute:

```text
Set up Development deployment

[ Register Deployment Agent ]

[ Create Development Environment ]
```

This must not appear when Deploy is disabled.

---

# 70. Finish onboarding

Final page:

```text
You're ready.

Organisation
Northstar Engineering

Owner
Rowan Smith

Project
Platform

Repository
example/platform

Review
Configured

[ Open Project ]
```

Route:

```text
/projects/platform
```

---

# 71. Onboarding persistence

Do not model onboarding purely as:

```text
step = 4
```

in frontend state.

Where possible, derive completion from real data:

```text
Organisation exists?

Owner exists?

Project exists?

Repository connected?

Review configured?
```

Persist only additional User-specific dismissal/completion state where necessary.

---

# 72. Interrupted onboarding

Example:

```text
Organisation created

Owner created

Project not created
```

On next login:

```text
Resume setup
```

starting at Project.

Do not recreate Organisation or Owner.

---

# 73. Skip onboarding

After Organisation and Owner are safely created, optional setup steps may be skipped.

User may navigate normally and return through:

```text
Setup Checklist
```

---

# 74. Setup checklist

Organisation or Project may display:

```text
Setup

✓ Owner account

✓ Project created

✓ Repository connected

✓ Review configured

○ Pipelines configured

○ Deploy configured
```

Only installed modules contribute checklist items.

The checklist is not a hard blocker.

---

# 75. Global navigation

Since only one Organisation exists, no Organisation switcher is required.

Top-level shell:

```text
Northstar Engineering

Overview

Projects

People

Settings

                    User Menu
```

---

# 76. Project navigation

Inside Project:

```text
Platform

Overview

Code

Review        if enabled

Pipelines     if enabled

Deploy        if enabled

Settings
```

Disabled modules are absent.

---

# 77. Project switcher

Provide:

```text
Platform ▼

Platform
Stockyards
Internal Tools

+ New Project
```

No Organisation selector is needed.

---

# 78. User menu

Example:

```text
Rowan Smith
@rowan

Profile

Preferences

Security

Sign Out
```

Owner may additionally see administrative settings through the normal Settings section rather than a separate platform-admin application.

---

# 79. People section

Top-level:

```text
People
├── Members
├── Teams
└── Invitations
```

---

# 80. Members page

Display:

```text
Name

Username

Email

Role

Teams

Status

Joined
```

Actions depending on permission:

```text
Invite

Change Role

Suspend

Remove
```

---

# 81. Teams page

Display:

```text
Team

Members

Projects
```

Actions:

```text
Create Team

Edit

Add Member

Remove Member
```

---

# 82. Settings

Top-level Organisation/instance settings:

```text
General

Members

Teams

Integrations

Modules

Audit

Security

Licensing

Danger Zone
```

Since:

```text
Organisation == Instance
```

these are effectively server-wide settings.

---

# 83. General settings

Configure:

```text
Organisation Name

Description

Avatar

Default preferences
```

Optional future:

```text
External URL

SMTP

Proxy configuration
```

---

# 84. Module settings

List installed modules:

```text
Review        Enabled

Pipelines     Enabled

Deploy        Disabled

Git           Disabled
```

Community/commercial capability information may also appear.

---

# 85. Licensing

Because one installation represents one Organisation, licensing naturally applies to the installation.

Example:

```text
Review
Commercial

Pipelines
Commercial

Deploy
Community
```

No Organisation selector is necessary in licence checks.

---

# 86. Permission architecture

Authorization still uses granular permissions.

Organisation-level examples:

```text
organisation.read

organisation.manage

users.read

users.manage

teams.manage

projects.create

integrations.manage

modules.manage

audit.read
```

Project-level:

```text
project.read

project.manage

project.members.manage

repository.read

repository.manage
```

Module permissions:

```text
review.*

pipelines.*

deploy.*
```

---

# 87. Role mapping

## Owner

Receives all administrative permissions.

## Admin

Receives most organisation/project management permissions except highly destructive Owner-only actions.

## Member

Receives basic Organisation access and Project/module access according to Project/Team grants.

Do not hard-code all authorization as:

```text
if Owner
```

Roles map to permissions.

---

# 88. Access evaluation

Typical Project request:

```text
Authenticated?
        ↓
Active Organisation Member?
        ↓
Project Accessible?
        ↓
Required Permission?
        ↓
Resource Accessible?
```

All checks occur server-side.

---

# 89. Organisation scope simplification

Because all data in the installation belongs to one Organisation, child tables generally do not require:

```text
OrganisationId
```

Example:

```text
Project

Id
Name
Slug
```

instead of:

```text
Project

Id
OrganisationId
Name
Slug
```

provided this invariant remains explicit.

Do not add redundant tenant IDs solely in anticipation of a hypothetical SaaS version.

---

# 90. Data boundary

The database itself is an Organisation boundary.

Separate organisations use:

```text
separate deployments

separate databases

separate secrets

separate integrations

separate licences
```

This substantially reduces risk of cross-tenant leakage.

---

# 91. Persistence

Core logical tables may include:

```text
core.organisation

core.users

core.user_profiles

core.credentials

core.sessions

core.memberships

core.invitations

core.teams

core.team_memberships

core.projects

core.project_user_access

core.project_team_access

core.repositories

core.repository_connections

core.user_repository_workspaces

core.integrations

core.audit_events

core.instance_configuration
```

There should be exactly one active:

```text
core.organisation
```

row.

---

# 92. Stable IDs

Use stable internal IDs.

Examples:

```text
UserId

ProjectId

RepositoryId

TeamId
```

Do not use:

```text
username

email

project slug

repository name

GitHub repository ID
```

as primary domain identity.

---

# 93. Slug uniqueness

Enforce:

```text
Project slug unique within installation

Repository slug unique within Project

Team slug unique within installation

Username unique within installation
```

Organisation slug is unnecessary initially.

---

# 94. Slug changes

Relationships use IDs.

Changing:

```text
Project slug
```

must not change Project identity.

Old URL redirects may be added later.

---

# 95. Project creation

Normal non-onboarding flow:

```text
Projects

[ New Project ]
```

Form:

```text
Name

Slug

Description

Visibility
```

After creation:

```text
Project Overview
```

---

# 96. Empty Project

Show:

```text
No repositories connected.

Connect a repository to start reviewing, testing, and deploying code.

[ Add Repository ]
```

---

# 97. Repository creation

From Project:

```text
Repositories

[ Add Repository ]
```

Possible providers:

```text
GitHub

Future:
GitLab
Azure Repos
Native Git
```

Also offer:

```text
Detect from local repository
```

where supported.

---

# 98. Multiple Projects

Example:

```text
Projects

Platform
Stockyards
Website
```

Each isolates:

```text
Repositories

Review configuration

Pipeline definitions

Deploy Applications
```

---

# 99. Multiple Repositories

Example:

```text
Project
Stockyards

Repositories

stockyards-api
stockyards-web
sma-control
```

Project-level modules may aggregate across all repositories.

---

# 100. User profile page

Example:

```text
Rowan Smith

@rowan

Software Engineer

Teams
Platform
Backend

Recent Activity
...
```

Only activity the viewer has permission to see should appear.

---

# 101. Profile settings

Sections:

```text
Profile

Preferences

Security
```

Profile:

```text
Display Name

Username

Avatar

Bio
```

Preferences:

```text
Theme

Timezone

Locale

Default Project
```

Security:

```text
Change Password

Sessions

API Tokens
```

---

# 102. User removal

Removing User from Organisation:

```text
revokes access

removes Team memberships

removes Project grants
```

but retains:

```text
Reviews

Comments

Audit records

Pipeline actions

Deployment actions
```

Historical attribution remains.

---

# 103. User suspension

Suspended User:

```text
cannot login

cannot use API tokens

cannot perform actions
```

Historical identity remains intact.

---

# 104. Owner removal

An Owner may be removed/demoted only if another Owner remains.

This must be enforced transactionally.

---

# 105. Organisation reset/delete

Since Organisation equals the installation, deletion is unusually destructive.

Prefer an explicit:

```text
Reset / Destroy Installation
```

operation rather than ordinary Organisation deletion.

Require strong confirmation.

Example:

```text
Type the organisation name:

Northstar Engineering
```

Potential behaviour:

```text
archive/purge database

remove configuration

return to Uninitialised state
```

This should be Owner-only.

Do not prioritise this for the first MVP unless necessary.

---

# 106. Project deletion

Project deletion/archive should notify modules through lifecycle events.

Example:

```text
ProjectDeleting
```

Review/Pipelines/Deploy handle their own data.

Core must not know module database schemas.

Prefer:

```text
Archive Project
```

before permanent deletion.

---

# 107. Repository deletion

Prefer:

```text
Disconnect Repository
```

or:

```text
Archive Repository
```

over destructive deletion.

Preserve engineering history.

---

# 108. Audit

Core audit events include:

```text
organisation.initialised

organisation.updated

user.created

user.suspended

user.disabled

user.role_changed

user.invited

user.invitation_accepted

team.created

team.updated

team.member_added

team.member_removed

project.created

project.updated

project.archived

repository.created

repository.connected

repository.disconnected

integration.created

integration.updated

module.enabled

module.disabled
```

Never audit raw:

```text
passwords

tokens

secret values
```

---

# 109. Activity vs audit

Audit:

```text
security/compliance/system record
```

Activity:

```text
human-readable product timeline
```

Example activity:

```text
Rowan created Platform.

Rowan connected example/platform.

Alice joined the organisation.
```

Do not expose every audit event as user-facing activity.

---

# 110. Core events

Core may publish:

```text
OrganisationInitialised

UserCreated

UserInvited

UserJoined

UserRemoved

TeamCreated

ProjectCreated

ProjectArchived

RepositoryConnected

RepositoryDisconnected
```

Modules may subscribe.

Avoid direct Core → module service calls.

---

# 111. Lifecycle events

Before destructive transitions:

```text
ProjectArchiving

RepositoryDisconnecting

UserRemoving
```

may allow modules to preserve/cleanup state.

Do not build an excessively complicated distributed transaction model.

---

# 112. Bootstrap API

Conceptually:

```text
GET /api/setup/status

POST /api/setup
```

Setup status:

```json
{
  "initialised": false
}
```

After setup:

```json
{
  "initialised": true
}
```

`POST /api/setup` must reject all requests after initialisation.

---

# 113. Core APIs

Indicative:

```text
/api/users/me

/api/users

/api/users/{id}

/api/organisation

/api/organisation/members

/api/organisation/invitations

/api/teams

/api/teams/{id}

/api/projects

/api/projects/{id}

/api/projects/{id}/members

/api/projects/{id}/teams

/api/projects/{id}/repositories

/api/repositories/{id}

/api/repositories/{id}/connection

/api/repositories/{id}/workspace

/api/integrations

/api/setup/status

/api/setup
```

Exact REST design may differ.

---

# 114. Security requirements

All authorization occurs server-side.

Frontend hiding is not permission enforcement.

Sensitive operations require:

```text
authentication

active User

active membership

permission

resource access
```

Owner-only destructive actions require explicit role/capability verification.

---

# 115. Integration secrets

Provider credentials:

```text
GitHub PAT

future API keys
```

must:

```text
be encrypted at rest

never appear in normal GET responses

never appear in logs

be replaceable

be revocable
```

---

# 116. First-run acceptance test

Start from:

```text
fresh database

no Organisation

no Users

InstanceState = Uninitialised
```

Open platform.

Verify:

```text
/setup shown
```

Create:

```text
Organisation
Northstar Engineering

Owner
Rowan Smith
@rowan
rowan@example.com
```

Verify:

```text
exactly one Organisation exists

User exists

Profile exists

Membership exists

Role = Owner

InstanceState = Initialised
```

Attempt setup again.

Verify:

```text
Rejected
```

---

# 117. First Project acceptance

During onboarding create:

```text
Project
Platform
```

Verify:

```text
Project exists

Owner can access Project

route works:
/projects/platform
```

---

# 118. Repository onboarding acceptance

Select:

```text
Local repository + GitHub
```

Enter local repository path.

Verify detection of:

```text
.git

branch

HEAD

origin
```

Connect GitHub credential.

Verify:

```text
Repository created

RepositoryConnection created

UserRepositoryWorkspace created

Repository data resolves successfully
```

---

# 119. Review onboarding acceptance

If Review is installed:

```text
Review configuration step appears
```

If Review is absent:

```text
Review step absent
```

Configure:

```text
Require one approval

Allow self review
```

Complete onboarding.

---

# 120. Second User acceptance

Owner creates invitation:

```text
alice@example.com

Role
Member
```

Alice accepts.

Verify:

```text
Alice User created

Alice Profile created

Alice Membership active

Role = Member

Alice is not Owner
```

---

# 121. Admin acceptance

Promote Alice:

```text
Member → Admin
```

Verify Admin can:

```text
create Project

invite Member

manage Team
```

but cannot perform Owner-only destructive actions.

---

# 122. Multiple Owner acceptance

Promote Alice:

```text
Admin → Owner
```

Verify:

```text
Rowan Owner

Alice Owner
```

Rowan may now:

```text
demote self
```

if permissions allow.

---

# 123. Last Owner acceptance

If Rowan is sole Owner:

```text
demote Rowan
```

must fail.

```text
remove Rowan
```

must fail.

```text
suspend Rowan
```

must fail if doing so would leave no active Owner.

---

# 124. Project access acceptance

Create:

```text
Private Project
SecretPlatform
```

Member Bob without explicit access requests it.

Result:

```text
Forbidden
```

Grant Team:

```text
Backend
```

access.

Add Bob to Backend.

Bob can now access Project.

---

# 125. Multiple Project acceptance

Create:

```text
Platform

Stockyards
```

Verify:

```text
repository lists isolated

Review configuration isolated

Pipeline definitions isolated

Deploy Applications isolated
```

---

# 126. Multiple Repository acceptance

Stockyards:

```text
stockyards-api

stockyards-web
```

Verify:

```text
both repositories belong to same Project

repository selector works

Project Review can aggregate both
```

---

# 127. User workspace acceptance

Rowan associates:

```text
C:\Development\stockyards-api
```

with:

```text
stockyards-api
```

Another User may have another local path.

Verify local workspace path is User-specific and does not alter Repository identity.

---

# 128. Module-aware navigation acceptance

With:

```text
Review enabled
Pipelines disabled
Deploy disabled
```

Project navigation shows:

```text
Overview
Code
Review
Settings
```

After enabling Pipelines:

```text
Overview
Code
Review
Pipelines
Settings
```

Deploy remains absent.

---

# 129. Setup checklist acceptance

After Review setup but before Pipelines setup:

```text
Setup

✓ Repository connected
✓ Review configured
○ Pipelines configured
```

Only if Pipelines is installed.

Dismissal should not block normal project use.

---

# 130. Database constraints

Enforce:

```text
one active Organisation

unique User email

unique username

unique Project slug

unique Team slug

unique Repository slug within Project

one active membership per User

one Team membership per User/Team

one direct Project access row per User/Project
```

Owner-count protection may require transactional domain logic.

---

# 131. Testing

## Unit

Cover:

```text
bootstrap state

Owner creation

role transitions

last Owner protection

Project visibility

Team access

User access

Repository ownership

repository status

workspace association

slug validation

invitation expiry

permission evaluation
```

## Integration

Cover:

```text
fresh bootstrap

second bootstrap rejection

Project creation

Repository connection

User invitation

role changes

Team membership

private Project denial

Team Project grant

multiple repositories

module-aware onboarding

module-aware navigation
```

---

# 132. Security tests

Explicitly verify:

```text
unauthenticated User cannot access application data

Member cannot promote self

Admin cannot perform Owner-only actions

final Owner cannot be removed

expired invite rejected

used invite rejected

bootstrap cannot run twice

Repository credentials never returned

User without private Project access receives Forbidden
```

---

# 133. Completion criteria

This Core foundation is complete when:

```text
One installation represents exactly one Organisation.

Fresh installations enter secure setup mode.

Setup creates the Organisation and first Owner.

Only one Organisation may exist.

The first User becomes Owner.

Multiple Owners are supported.

The final Owner is protected.

Users have profiles.

Owners/Admins can invite Users.

Users can be suspended.

Teams can be created.

Users can belong to Teams.

Projects can be created.

Projects support Private and Organisation visibility.

Project access may be granted to Users and Teams.

Projects may contain multiple Repositories.

Repositories are provider-independent.

GitHub can be connected without GitHub types leaking into Core.

Users may associate local repository workspaces.

Multiple local workspace paths are possible across Users.

Enabled modules extend onboarding.

Enabled modules extend navigation.

Disabled modules remain absent.

Audit captures meaningful administrative actions.

The platform can onboard its own Organisation, Owner, Project, and Repository from a completely fresh installation.
```

---

# 134. Guiding hierarchy

The product model is:

```text
INSTALLATION
      =
 ORGANISATION
      │
      ├── Users
      │    └── Profiles
      │
      ├── Teams
      │
      ├── Projects
      │     │
      │     ├── Repositories
      │     │
      │     ├── Review
      │     │
      │     ├── Pipelines
      │     │
      │     └── Deploy
      │
      ├── Integrations
      │
      ├── Modules
      │
      ├── Audit
      │
      └── Settings
```

Access flows:

```text
User
  ↓
Organisation Membership
  ↓
Project Access
  ↓
Module Permission
  ↓
Resource
```

First-run flows:

```text
Fresh Installation
      ↓
Organisation
      ↓
Owner
      ↓
Project
      ↓
Repository
      ↓
Enabled Module Setup
      ↓
Ready
```

The implementation should optimise for:

> A new self-hosted installation can go from an empty database to a correctly owned Organisation, real User account, Project, connected Repository, and usable Review workspace in a few minutes, without introducing multi-tenant complexity that the self-hosted product does not need.
