# ForgeDeck licensing

| Tree | Edition | License |
|------|---------|---------|
| [`community/`](community/) + [`sdk/`](sdk/) | Community | **AGPL-3.0-only** |
| [`commercial/`](commercial/) | Team + Enterprise (proprietary) | [LICENSE.enterprise](LICENSE.enterprise) / [commercial/LICENSE](commercial/LICENSE) |

**Have the commercial licence text reviewed by a software-licensing lawyer before public release.**

## Community

```bash
dotnet run --project community/src/Server/ForgeDeck.Server
FOSS_ONLY=1 dotnet run --project community/src/Server/ForgeDeck.Server
```

## Team

```bash
dotnet run --project community/src/Server/ForgeDeck.Server -p:IncludeTeam=true
```

## Enterprise (transitional host flag)

```bash
dotnet run --project community/src/Server/ForgeDeck.Server -p:IncludeEnterprise=true
```

Prefer `Dockerfile.commercial` for air-gapped commercial deployments: one image, entitlements from the signed licence ([docs/architecture/entitlements.md](docs/architecture/entitlements.md)).

Runtime entitlement (`forgedeck.lic`) is separate from source licensing. See [docs/architecture/commercial-protection.md](docs/architecture/commercial-protection.md).

## Rules

1. Proprietary product code belongs only under `commercial/`.
2. Community must never take a compile-time dependency on `commercial/` (except Server conditional IncludeTeam/IncludeEnterprise — transitional).
3. Removing `commercial/` leaves a viable Community product.
4. Do not encode a global Team/Enterprise edition in application code — use per-module entitlements / capabilities.
