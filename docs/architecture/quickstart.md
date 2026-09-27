# Architecture quickstart

ForgeDeck is a .NET 10 modular monolith: one host process, independently entitled modules (Git → Review → Build → Deploy), Community soft limits, and commercial deltas unlocked by a signed `forgedeck.lic`.

## Layout

| Path | Role |
|------|------|
| `sdk/ForgeDeck.Contracts` | Public contracts (modules, capabilities, events) |
| `community/src/Core` | Platform core + messaging |
| `community/src/Server` | Host (`Program.cs`) + SPA (`wwwroot`) |
| `community/src/Modules/*` | Product modules |
| `community/src/Connectors` | First-party source providers (GitHub) |
| `community/src/Runner` | Out-of-process pipeline agent |
| `commercial/{team,enterprise}` | Capability deltas (AGPL stays in `community/`) |

## Boot path

1. `ModuleDiscovery` loads `ForgeDeck.*.dll` modules (and optional commercial packages).
2. Each `IPlatformModule` registers DI + endpoints.
3. Platform + messaging schemas bootstrap (`EnsureCreated` / module `EnsureSchema` + `SchemaBootstrap` version bookkeeping).
4. Session auth middleware, rate limits, then module maps.

## Cross-module rules

- **No** direct references between optional module *implementations*.
- Share via `*.Contracts` events and SDK provider interfaces.
- Architecture tests enforce Community ↛ commercial and optional-module isolation.

## Editions

| Build | How |
|-------|-----|
| Community | Default Server project |
| Team host | `-p:IncludeTeam=true` |
| Enterprise host | `-p:IncludeEnterprise=true` |
| FOSS-only | `FOSS_ONLY=1` |

Runtime entitlements still come from the licence file — compile flags only decide which assemblies are on disk.

## Dig deeper

- [open-core.md](open-core.md) · [events.md](../events.md) · [schema-versioning.md](schema-versioning.md)
- [feature-matrix.md](feature-matrix.md) · [dependency-rules.md](dependency-rules.md) · [naming.md](naming.md)
