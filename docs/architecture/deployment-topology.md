# Deployment topology

> **Licensing does not dictate topology.**  
> Entitlements enable Git/Code/Review/Build/Deploy independently.  
> Process layout follows scale, security, and failure isolation.

## Logical domains

```text
PLATFORM CORE
  Auth • Projects • RBAC • Licensing • Audit • Events • Module registry

SCM DOMAIN (often one deployable initially)
  Git • Code • Review     ← separately entitled capabilities

BUILD (independently deployable from day one)
  Coordinator • Scheduler • Runners

DEPLOY (independently deployable from day one)
  Controller • Environments • Site agents
```

| Module | Independently deployable? | Why |
|--------|---------------------------|-----|
| Git | Yes (eventually clustered) | Disk/network IO |
| Code | With SCM / later | Coupled to Git |
| Review | With SCM / later | Coupled to Git/Code/projects |
| Build | **Yes** | Untrusted workloads, CPU/RAM |
| Deploy | **Yes** | Trust/network boundary; air-gap agents |

## Profiles

Constants: `DeploymentProfiles` in `ForgeDeck.Contracts.Topology`.

### `appliance` (Community / small Team)

Single process/container — `docker compose up` / default `compose.yaml`.

### `team-split`

```text
Server 1: Core + SCM (Git/Code/Review)
Server 2: Build + runners
Server 3: Deploy
```

See `compose.team-split.yaml`.

### `enterprise`

HA Core/API, Git nodes, Build controller + pools, Deploy controller + site agents. Topology varies; contracts stay the same.

## Database ownership (hard rule)

**Modules must not share schemas.** Prefer separate databases even on one PostgreSQL/SQLite server:

```text
platform_db   → Core only
git_db        → Git only
review_db     → Review only
build_db      → Build only
deploy_db     → Deploy only
```

```text
Review → Review DB
Review → IGitService / events
```

Never:

```text
Review → SELECT * FROM git.internal_…
```

Connection string keys: `ModuleDatabases.ConnectionKeys` (`Platform`, `Git`, `Review`, `Build`, `Deploy`).  
Profile: `Deployment:Profile` / env `FORGEDECK_DEPLOYMENT_PROFILE` (`DeploymentProfileResolver`).

Today the appliance often points every key at one SQLite file; ownership is still logical — do not cross-query. Split files/servers when a profile requires isolation.

## Service contracts

Callers use `IGitService`, `ICodeService`, `IReviewService`, `IBuildService`, `IDeployService` from `sdk/ForgeDeck.Contracts/Services`. In-process adapters exist for Git/Review/Build/Deploy; remoting can replace adapters without changing SKUs.

## Related

- [module-contract.md](module-contract.md) — package/extension model
- [entitlements.md](entitlements.md) — per-module licensing
- [feature-matrix.md](feature-matrix.md) — capability maturity
