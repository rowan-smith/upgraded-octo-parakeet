# Architecture guide

ForgeDeck is a modular monolith. A module is independently enabled, owns its state, and exposes only contracts needed by other modules.

## Dependency rules

1. Domain code depends on the base class library only.
2. Application code depends on its Domain and public contracts.
3. Infrastructure implements Application ports.
4. API code translates HTTP requests into Application calls and owns authorization at the boundary.
5. The module root is the composition root. It registers services, routes, capabilities, navigation, and resource extensions.
6. An optional module never references another optional module.
7. Cross-module reactions use domain events. Cross-module reads use provider interfaces.

The build demonstrates this with `ChangeOpened`, `PushReceived`, `ISourceProvider`, and `ICheckProvider`. Native Git and the GitHub connector satisfy the same source contract. Pipelines handles stable source/review events and exposes check results. Disabling any optional module does not prevent the others from starting.

## Pragmatic MVP boundaries

The repositories, event publisher, audit store, and runner are in-memory adapters. The deterministic runner models sequential execution without allowing arbitrary server-side shell execution. Production adapters can replace these through existing interfaces without changing the domain or API layers.

Keep abstractions demand-driven: introduce a port when a real second implementation or an architectural boundary needs it, not in anticipation of possible future work.
