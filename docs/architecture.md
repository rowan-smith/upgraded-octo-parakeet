# Architecture guide

ForgeDeck is a modular monolith with an open-core source split. See:

- [open-core.md](architecture/open-core.md) — CE/EE boundaries
- [modules.md](architecture/modules.md) — module ownership
- [connectors.md](architecture/connectors.md) — connectors vs modules
- [extensions.md](architecture/extensions.md) — manifests and licensing triad
- [dependency-rules.md](architecture/dependency-rules.md) — hard invariants

## Dependency rules (summary)

1. Domain code depends on the base class library only.
2. Application code depends on its Domain and public contracts.
3. Infrastructure implements Application ports.
4. API code translates HTTP requests into Application calls and owns authorization at the boundary.
5. The module root is the composition root. It registers services, routes, capabilities, navigation, and resource extensions.
6. An optional module never references another optional module.
7. Cross-module reactions use domain events. Cross-module reads use provider interfaces.
8. CE never depends on EE.

The build demonstrates this with `ChangeOpened`, `PushReceived`, `ISourceProvider`, and `ICheckProvider`. Native Git and the GitHub connector satisfy the same source contract. Build (runtime id `pipelines`) handles stable source/review events and exposes check results. Disabling any optional module does not prevent the others from starting.

## Pragmatic MVP boundaries

The repositories, event publisher, audit store, and runner are in-memory or SQLite adapters as appropriate. Keep abstractions demand-driven: introduce a port when a real second implementation or an architectural boundary needs it.
