# ForgeDeck Enterprise Edition (`/ee`)

This directory is the proprietary, source-available Enterprise Edition (EE),
following the same open-core separation model as GitLab's `/ee` tree.

- **Community Edition (CE):** everything outside `/ee` — AGPL-3.0-only
- **Enterprise Edition (EE):** everything under `/ee` — [EE License](LICENSE)

## Behaviour

- A CE build omits `/ee` assemblies (default).
- An EE build includes `/ee` but, without a valid signed entitlement, behaves
  like CE for commercial capabilities (features stay gated).
- Deleting `/ee` or building with `FOSS_ONLY=1` yields CE-only behaviour.

## Layout

```text
ee/
├── LICENSE                          # EE proprietary license
└── Modules.Review.Commercial/       # Review EE capabilities
```

Add future sellable modules under `ee/` only — never under `src/`.
