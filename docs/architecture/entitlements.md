# Module entitlements (air-gapped licensing)

> **Target model.** ForgeDeck does not have a global “Team installation” or “Enterprise installation.”
> An installation has **five module entitlements**. Bundles are SKUs that expand to those entitlements.

See [feature-matrix.md](feature-matrix.md) for Community soft limits and per-module maturity tiers.

See [feature-matrix.md](feature-matrix.md) for Community soft limits and per-module maturity tiers.

## Rules

1. Application code checks **capabilities** or `IEntitlementService.GetEntitlement(module)`, never `if (IsEnterprise)`.
2. No commercial licence → every module is Community.
3. Removing or expiring a commercial licence returns the installation to Community; it does not brick Git/Review.
4. Soft seat overage warns administrators; it does not lock users out.
5. Maintenance expiry stops **updates/support eligibility**, not runtime feature rights (unless `validUntil` / per-module `expiresAt` is set).
6. Community soft limits (1 approval rule / 1 concurrent build / 1 environment) beat metered transaction caps.

## Entitlement resolution

```text
Licence? ──null──► Community for all modules
    │
    ▼
Validate signature (vendor public key)
Validate instance binding (optional)
Validate validUntil / module expiresAt
    │
    ▼
Per-module EntitlementLevel (Community | Team | Enterprise)
    │
    ▼
ModuleEntitlementTables → capability set
    │
    ▼
∩ installed commercial package declarations
    │
    ▼
capabilities.Has(...)
```

## Offline challenge / response

```bash
# On air-gapped host
GET /api/licensing/request   → licence-request.nlr (JSON)

# Customer portal (internet) binds subscription + request → signed licence

# Back on air-gapped host
POST /api/licensing/commercial  { "payload": "<signed forgedeck.lic JSON>" }
```

Usage export for seat reconciliation: `GET /api/licensing/usage-export`.

## SKUs

See [packaging/bundles/README.md](../../packaging/bundles/README.md). `LicenceSkuCatalog` expands SKUs into module maps.

## Signing

Licences are signed with **Ed25519**. The installation embeds only the vendor public key (`ForgeDeckLicenceKeys`). The private key never ships with the product.

## Images

| Image | Contents |
|-------|----------|
| `Dockerfile` | Community host (no commercial assemblies) |
| `Dockerfile.commercial` | **Identical full binary** with Team+Enterprise packages; licence decides entitlements |

Compile-time `IncludeTeam` / `IncludeEnterprise` remain for FOSS CI and transitional builds — prefer the commercial image + licence for production air-gap deployments.
