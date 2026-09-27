# Architecture guide

ForgeDeck is a modular monolith with an open-core source split. Core ships first; product Modules and Connectors are installed later as needed.

## High-level shape

```text
ForgeDeck Core
│
├── Organisation
├── Users
├── Teams
├── Projects
├── RBAC
├── Licensing
├── Audit
├── Extension Runtime
└── Event Runtime
     │
     ├──────────── Installable Modules
     │              ├── Git
     │              ├── Code
     │              ├── Review
     │              ├── Build
     │              └── Deploy
     │
     └──────────── Installable Connectors
                    ├── GitHub
                    ├── GitLab
                    ├── Entra
                    ├── Jira
                    └── ...
```

## Lifecycle

```text
Install ForgeDeck
       ↓
Configure Core
       ↓
Organisation ready
       ↓
Install Modules/Connectors as needed
       ↓
Modules register:
    routes
    navigation
    permissions
    capabilities
    event handlers
    event contracts
    settings
    providers
       ↓
ForgeDeck progressively grows
```

First-run onboarding configures **Core only**. Modules and Connectors are installed afterwards from Settings.

## First-run onboarding

```text
Bootstrap
   ↓
Organisation
   ↓
Licence
   ↓
Owner
   ↓
Optional Members
   ↓
Optional First Project
   ↓
Complete
```

After setup, Organisation Settings expose:

```text
Settings
├── Modules
└── Connectors
```

Administrators install Git, Code, Review, Build, and Deploy only when they want them. Product Modules do **not** contribute first-run onboarding steps. Post-install project/module configuration belongs in Settings (or a future post-install configuration contributor), not the setup wizard.

## Module availability vs project configuration

Server-level axes:

```text
Installed
Enabled
Licensed
Configured
```

Initial rule:

```text
Module installed + enabled on server
        ↓
Module available to all Projects
```

Individual Projects are then simply **configured** or **not configured** for that Module (for example Build pipelines defined for Atlas but not for Website). There is no separate per-project enablement lifecycle for now. Per-project enablement can be added later if an isolation requirement appears.

## Event platform rule

Everything reacts to Events. There is no separate command bus, user-event bus, system-event bus, or external-event bus. Core owns the envelope, transport, outbox, inbox, retries, correlation, and causation. Modules own event semantics, payloads, and handlers. See [events.md](architecture/events.md).

## Source split

```text
community/   AGPL Community (Core, modules, connectors, Server, Runner, tests)
sdk/         Shared contracts (ships with Community)
commercial/  Proprietary Team + Enterprise deltas
```

Community never references Commercial. Enterprise builds on Team. See [open-core.md](architecture/open-core.md).

## Topic index

- [open-core.md](architecture/open-core.md) — CE/EE boundaries
- [modules.md](architecture/modules.md) — module ownership (including Code)
- [connectors.md](architecture/connectors.md) — connectors vs modules
- [extensions.md](architecture/extensions.md) — manifests and Installed / Enabled / Licensed / Configured
- [dependency-rules.md](architecture/dependency-rules.md) — hard invariants
- [events.md](architecture/events.md) — Universal Event Architecture
- [entitlements.md](architecture/entitlements.md) — offline licensing
- [module-contract.md](architecture/module-contract.md) — package contract
- [feature-matrix.md](architecture/feature-matrix.md) — capability maturity
- [naming.md](architecture/naming.md) — runtime vs licence ids

Domain/UI specs that expand on this model: [onboard.md](onboard.md), [nav-structure.md](nav-structure.md), [events.md](events.md) (feature specification).

## Dependency rules (summary)

1. Domain code depends on the base class library only.
2. Application code depends on its Domain and public contracts.
3. Infrastructure implements Application ports.
4. API code translates HTTP requests into Application calls and owns authorization at the boundary.
5. The module root is the composition root. It registers services, routes, capabilities, navigation, and resource extensions.
6. An optional module never references another optional module.
7. Cross-module reactions use domain events. Cross-module reads use provider interfaces.
8. CE never depends on EE.

The build demonstrates this with stable source/review events and provider interfaces. Native Git and the GitHub connector satisfy the same source contract. Disabling any optional module does not prevent the others from starting.

## Pragmatic MVP boundaries

The repositories, event publisher, audit store, and runner are in-memory or SQLite adapters as appropriate. Keep abstractions demand-driven: introduce a port when a real second implementation or an architectural boundary needs it.
