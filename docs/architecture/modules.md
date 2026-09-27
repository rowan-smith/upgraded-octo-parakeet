# Modules

```text
community/src/Modules/          Community product Modules
commercial/team/Modules/        Team deltas (*.Team)
commercial/enterprise/Modules/  Enterprise deltas (*.Enterprise)
```

## First-party product Modules

| Product | Project(s) | Runtime id | Notes |
|---------|------------|------------|-------|
| Git | `ForgeDeck.Git` (+ `.Contracts`) | `git` | Native repository hosting / transport |
| Code | `ForgeDeck.Code` (+ `.Contracts`, `.Web`) | `code` | Source browsing / navigation — **target** layout; treat as a real Module even when much of the UX is frontend/provider-oriented |
| Review | `ForgeDeck.Review` (+ Team / Enterprise) | `review` | Changes, approvals, checks projection |
| Review Team | `ForgeDeck.Review.Team` | `review-team` | Multi-approval / code owners |
| Review Enterprise | `ForgeDeck.Review.Enterprise` | `review-enterprise` | Path / SoD / compliance |
| Build | `ForgeDeck.Build` (+ Team / Enterprise) | `pipelines` (licence id `build`) | YAML pipelines, runners |
| Deploy | `ForgeDeck.Deploy` (+ Team / Enterprise) | `deploy` | Environments, releases |

Former runtime id `review-commercial` is superseded by `review-team` / `review-enterprise` (config may still list the alias for migration).

Enterprise may depend on Team. Do not duplicate Community Review engines.

## Code as a Module

Code is an independently installable and licensable product Module (`git` / `code` / `review` / `build` / `deploy`). Target layout:

```text
community/src/Modules/Code/
    ForgeDeck.Code.Contracts
    ForgeDeck.Code
    ForgeDeck.Code.Web
```

Even if most of the implementation is frontend- and provider-oriented, Code must remain a Module architecturally — licensed, listed, and installed like the others — not only a catalogue / SPA concern.

## Availability (no per-project enablement)

```text
Module installed + enabled on server
        ↓
Module available to all Projects
```

Projects are then **configured** or **not configured** for that Module. Do not introduce a separate per-project enablement axis for now. See [../architecture.md](../architecture.md) and [extensions.md](extensions.md).

## Installation vs first-run

Modules are **not** part of first-run Core onboarding. After Organisation setup:

```text
Settings
├── Modules
└── Connectors
```

Post-install configuration (policies, first pipeline, environments) belongs in Settings or a future **post-install configuration contributor** — not `IOnboardingContributor` steps in the setup wizard.

First-party modules declare the same package contract as third parties (`module.json` / `ModuleManifest` permissions, events, extension points). See [module-contract.md](module-contract.md).
