# Open-core / commercial architecture

```text
community/   Community open source (AGPL)
sdk/         Shared extension contracts (AGPL; ships with Community)
commercial/  Proprietary Team + Enterprise implementations
```

## Dependency direction

```text
Community → SDK
Team → SDK + Community contracts/modules
Enterprise → SDK + Team (+ Community contracts)
Community ──X──► commercial/
```

## Licensing model (target)

See [entitlements.md](entitlements.md). **Per-module entitlement levels**, not a global edition flag.
Bundles/SKUs expand to module maps. Runtime checks capabilities / `GetEntitlement(module)`.

## Host flags (transitional)

| Flag | Ships |
|------|--------|
| (default) | Community only |
| `-p:IncludeTeam=true` | + Team packages |
| `-p:IncludeEnterprise=true` | + Team + Enterprise |
| `FOSS_ONLY=1` | Forces Community |

These flags remain for FOSS CI and local builds. **Production commercial / air-gap delivery** should use `Dockerfile.commercial` (full packages in one image) and unlock features via the signed licence — not separate Team vs Enterprise images.

## Related

- [module-contract.md](module-contract.md)
- [deployment-topology.md](deployment-topology.md)
- [entitlements.md](entitlements.md)
- [feature-matrix.md](feature-matrix.md)
- [commercial-protection.md](commercial-protection.md)
- [modules.md](modules.md)
- [extensions.md](extensions.md)
- [dependency-rules.md](dependency-rules.md)

## Future

A hollow `ForgeDeck.Abstractions` project under `sdk/` is deferred. Shared contracts live in `ForgeDeck.Contracts` for now.
