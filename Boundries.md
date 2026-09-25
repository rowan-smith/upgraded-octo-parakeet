# Premium boundaries

Canonical feature matrix: [docs/architecture/feature-matrix.md](docs/architecture/feature-matrix.md).

Tiers follow **capability maturity**. Community runs a real small stack; paid tiers add collaboration then governance/scale.

## Soft Community limits (preferred)

| Module | Limit | Meaning |
|--------|-------|---------|
| Review | 1 approval rule | `main` can require 1 approval; not “one PR ever” |
| Build | 1 concurrent pipeline | Unlimited history; only one run at a time |
| Deploy | 1 environment | e.g. a single Production target |

Do **not** use monthly transaction caps for air-gapped products.

## Editions (per module)

| | Community | Team | Enterprise |
|---|---|---|---|
| Audience | Hobby / small internal | Engineering teams | Regulated / large |
| Review | Basic PR + 1 approval rule | Multi-approval, CODEOWNERS, workflow | Compliance / SoD / policy |
| Build | Basic CI + 1 concurrent | Parallel team CI | Verified / HA CI |
| Deploy | Basic CD + 1 environment | Multi-env CD | Multi-site governed CD |
| Git / Code | Full basic | Collaboration / intelligence | Scale + governance |

Modules are independently licensable; suite SKUs grant all five at once. Entitlements stack additively.
