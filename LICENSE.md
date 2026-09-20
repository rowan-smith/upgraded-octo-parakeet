# ForgeDeck licensing (GitLab-style open core)

ForgeDeck uses the same consumer / commercial source separation model as GitLab:

| Tree | Edition | License |
|------|---------|---------|
| Everything **outside** `/ee` | Community Edition (CE) | **AGPL-3.0-only** — [licenses/AGPL-3.0.txt](licenses/AGPL-3.0.txt) |
| Everything **under** `/ee` | Enterprise Edition (EE) | **EE License** (proprietary, source-available) — [ee/LICENSE](ee/LICENSE) |

## Community Edition (CE)

Default build. Public / self-hostable under AGPL-3.0-only.

```bash
dotnet run --project src/Platform.Api
# or explicitly:
FOSS_ONLY=1 dotnet run --project src/Platform.Api
```

## Enterprise Edition (EE)

Includes `/ee` assemblies. Production use of EE code requires a commercial
subscription / agreement (see `ee/LICENSE`). Runtime commercial capabilities
also require a signed entitlement file.

```bash
dotnet run --project src/Platform.Api -p:IncludeEE=true
```

Without a valid entitlement, an EE build still behaves like CE for gated
capabilities (same idea as unlicensed GitLab EE).

## Rules

1. Sellable / proprietary product code belongs only under `/ee`.
2. CE must never take a compile-time dependency on `/ee`.
3. AGPL does not grant rights to `/ee` source; the EE License does not relicense CE.
4. You may remove `/ee` after cloning to obtain a CE-only tree.

## Contributors

- Contributions outside `/ee` are under AGPL-3.0-only.
- Contributions under `/ee` are under the EE License (or a contributor agreement).
