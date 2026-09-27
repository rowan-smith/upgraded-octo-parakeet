# Dependency rules

1. Community assemblies do not reference commercial assemblies.
2. Community projects under `community/` do not reference `commercial/` (Server may conditionally IncludeTeam/IncludeEnterprise).
3. Optional Community **implementation** assemblies do not reference each other (`ForgeDeck.Review` ↛ `ForgeDeck.Build`, etc.).
4. Peer **`*.Contracts`** references are allowed for cross-module events and DTOs (e.g. `ForgeDeck.Build` → `ForgeDeck.Git.Contracts` / `ForgeDeck.Review.Contracts`). Never reference another module’s implementation project.
5. Cross-module integration uses events (`*.Contracts` + `ForgeDeck.Messaging`) and provider interfaces in `sdk/ForgeDeck.Contracts`.
6. Team/Enterprise prefer public contracts over Infrastructure internals.
7. **Licensing ≠ topology** — entitlements enable capabilities; process layout follows scale/security ([deployment-topology.md](deployment-topology.md)).
8. **No shared module schemas** — Review never queries Git tables; use `IGitService` / events and separate DBs (`ModuleDatabases`).
9. **Core** must not reference optional Module implementation assemblies (`ForgeDeck.Review`, `ForgeDeck.Build`, `ForgeDeck.Deploy`, `ForgeDeck.Git`, `ForgeDeck.Code`).
10. **Module API routes** are declared on `ModuleManifest.ApiRoutePrefixes` (generic gate — no Core switch on module names).
11. **Canonical Build runtime identity** is `build`. Do not persist `pipelines` as a runtime Module id (legacy alias/migration only).
12. **Module Events** use stable `Type` + `Version` (`forgedeck.*`); CLR naming is a review convention, not a physical enforcer.
13. **Code** consumes source-provider abstractions; it must not reference `ForgeDeck.Git` implementation.
14. **Deploy** execution goes through `IDeploymentExecutor`.
15. **Git** storage goes through `IGitObjectStore`.

See [events.md](events.md) for envelope, outbox/inbox, and package layout.

Tests: `community/tests/Core.Tests/ArchitectureTests.cs` (CI via Core.Tests), `commercial/tests/*`.

CI `community` job deletes `commercial/` and must still build.
