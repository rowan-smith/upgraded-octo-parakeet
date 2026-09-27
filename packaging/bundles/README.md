# Commercial licence SKUs and bundles

There is no special “Team installation.” SKUs expand to per-module entitlements.

| SKU | Effect |
|-----|--------|
| `TEAM-BUNDLE` | All five modules → Team |
| `ENTERPRISE-BUNDLE` | All five modules → Enterprise |
| `REVIEW-TEAM` / `REVIEW-ENTERPRISE` | Review only |
| `BUILD-TEAM` / `BUILD-ENTERPRISE` | Build only |
| `DEPLOY-TEAM` / `DEPLOY-ENTERPRISE` | Deploy only |
| `GIT-TEAM` / `GIT-ENTERPRISE` | Git only — **reserved/planned** (no commercial package; capability sets empty) |
| `CODE-TEAM` / `CODE-ENTERPRISE` | Code only — **reserved/planned** (no commercial package; capability sets empty) |
| `BUILD-DEPLOY-TEAM` | Build+Deploy Team; others Community |

Shipped commercial soft unlocks today: `Review.MultiApproval`, `Build.Concurrent`, `Deploy.MultiEnvironment`. Enterprise module packages currently declare no capabilities.

Generate a document (then sign with `LicenceSigner`):

```csharp
var doc = LicenceSkuCatalog.CreateDocument("TEAM-BUNDLE", organisationId, instanceId, maxUsers: 250);
```

Or via CLI (after signing key is available):

```text
LicenceSigner keys/licence-private.pem unsigned.json forgedeck.lic
```

Runtime rights come from the signed file. Package possession ≠ entitlement.
