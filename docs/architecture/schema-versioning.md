# Schema versioning

ForgeDeck currently uses **shared SQLite** with multiple `DbContext`s (platform, git, review, build, deploy, messaging). Module schemas are bootstrapped with `EnsureCreated` / `EnsureSchema` (`CreateTables` when missing, plus a few additive `CREATE TABLE IF NOT EXISTS` patches) — not EF Migrations.

## Why not EF Migrations yet

Several contexts share one file (`ConnectionStrings:*` often point at `data/forgedeck.db`). EF Migrations assume one model owns a database; running multiple migration histories against the same SQLite file conflicts. Full EF Migrations are deferred until module databases can split cleanly.

## Honest substitute: `forgedeck_schema_versions`

After each component’s bootstrap, call `SchemaBootstrap.Record(connection, component, version)` (`ForgeDeck.Core.Persistence`). That upserts:

| column | meaning |
|--------|---------|
| `component` | e.g. `platform`, `git`, `review`, `build`, `deploy` |
| `version` | integer (today `SchemaBootstrap.PlatformSchemaVersion`) |
| `applied_at` | UTC ISO-8601 timestamp of last record |

This is **not** an upgrade engine: it does not apply diffs or refuse mismatched versions. It records which bootstrap path last touched the file so operators and future migration work have a clear starting point.

## Related

- [dependency-rules.md](dependency-rules.md) — no shared module table queries
- [deployment-topology.md](deployment-topology.md) — appliance vs split topology
