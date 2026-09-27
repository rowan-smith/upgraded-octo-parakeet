# Architecture boundaries

Short index of where platform / module / commercial lines are documented.

| Doc | Covers |
|-----|--------|
| [open-core.md](open-core.md) | `community/` vs `sdk/` vs `commercial/`, dependency direction, host flags |
| [dependency-rules.md](dependency-rules.md) | Assembly reference rules, contracts vs implementations, no shared module schemas |
| [module-contract.md](module-contract.md) | Module host, package document, permissions, events |
| [commercial-protection.md](commercial-protection.md) | Source / legal / distribution boundaries for proprietary packages |
| [schema-versioning.md](schema-versioning.md) | Shared SQLite bootstrap + schema version table (not EF Migrations yet) |
| [naming.md](naming.md) | Runtime vs licence module id aliases (e.g. `pipelines` → `build`) |

Community never references commercial assemblies. Cross-module integration uses events and SDK contracts — not peer Infrastructure projects.
