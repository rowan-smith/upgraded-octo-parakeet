# Dependency rules

1. Community assemblies do not reference commercial assemblies.
2. Community projects under `community/` do not reference `commercial/` (Server may conditionally IncludeTeam/IncludeEnterprise).
3. Optional Community modules do not reference each other.
4. Cross-module integration uses events and provider interfaces in `sdk/ForgeDeck.Contracts`.
5. Team/Enterprise prefer public contracts over Infrastructure internals.
6. **Licensing ≠ topology** — entitlements enable capabilities; process layout follows scale/security ([deployment-topology.md](deployment-topology.md)).
7. **No shared module schemas** — Review never queries Git tables; use `IGitService` / events and separate DBs (`ModuleDatabases`).

Tests: `community/tests/Core.Tests/ArchitectureTests.cs`, `commercial/tests/*`.

CI `community` job deletes `commercial/` and must still build.