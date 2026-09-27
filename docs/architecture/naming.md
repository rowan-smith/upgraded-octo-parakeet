# Naming aliases

Platform code uses a few intentional id aliases. Prefer the **licence / entitlement** id in licence documents and capability checks; prefer the **runtime** id in module manifests, nav routes, and host registration.

| Concern | Id | Notes |
|---------|----|--------|
| Build module licence / entitlement | `build` | `PlatformModules.Build`; used in signed licences and `GetEntitlement` |
| Build module runtime | `pipelines` | `BuildModule` manifest id, nav keys, `/pipelines` routes |

`PlatformModules.Normalize` maps `pipelines` → `build` (and strips `-team` / `-enterprise` / `-commercial` suffixes). Messaging consumer prefixes use the same rule (`forgedeck.build.*` even when the runtime id is `pipelines`).
