# Feature matrix (capability maturity)

Tiers are about **capability maturity**, not disabling the product. Community must run a real small DevOps stack.
Team adds collaboration/workflow. Enterprise adds governance, scale, HA, security, and compliance.

> Prefer soft structural limits over metering.  
> Review: **1 approval rule** (not “1 PR”).  
> Build: **1 concurrent pipeline** (not “N builds/month”).  
> Deploy: **1 environment** (not “N deployments/month”).

Licence enforcement is not the primary OSS moat — Team/Enterprise should contain substantial capabilities (identity, policy, HA, multi-site, compliance). Numerical Community limits are UX boundaries, not DRM.

## Platform tiers

| | Community | Team | Enterprise |
|---|---|---|---|
| Target | Individual / hobby / small internal | Normal engineering teams | Large / regulated orgs |
| Deployment | Single instance | Single/multi-node | HA / multi-site |
| Users | Soft guidance (~5–10) | Licensed seats (soft warn) | Licensed seats / EA |
| Auth | Local accounts | Local + OIDC | OIDC/SAML/LDAP/AD/SCIM |
| RBAC | Basic roles | Granular project roles | Custom roles / policy |
| Audit | Basic activity | Full audit history | Immutable/compliance audit |
| Air-gap licence | None required | Signed offline licence | Signed offline licence |

Modules are **independently entitled**. Entitlements are **additive** (e.g. Team Suite + Deploy Enterprise).

## Community soft limits

| Module | Community ceiling | Team unlock |
|--------|-------------------|-------------|
| **Review** | 1 approval rule (`minimumApprovals ≤ 1`) | `Review.MultiApproval` (+ workflow) |
| **Build** | 1 concurrent pipeline | `Build.Concurrent` |
| **Deploy** | 1 environment | `Deploy.MultiEnvironment` |
| **Git / Code** | Full basic hosting/browse | Collaboration / intelligence |

Constants: `CommunityLimits` in `ForgeDeck.Contracts`.

## Module summaries

### Git
Least restricted foundation. Community: repos, SSH/HTTPS, clone/push, basic permissions. Team: protected branches, push rules, quotas. Enterprise: mirroring, multi-site, compliance retention.

### Code
Community: respectable file/diff/blame/search. Team: repo-wide search, code intelligence. Enterprise: org-wide search, ownership enforcement. May ship as part of Git commercially while remaining modular internally.

### Review
- **Community:** unlimited PRs + discussions + merge strategies + **1 required approval rule**
- **Team:** multi-approval, reviewer groups, CODEOWNERS, merge queue, advanced branch policies
- **Enterprise:** SoD, approval chains, org-wide policy, signed approvals, compliance export

### Build
- **Community:** YAML CI, logs, artifacts, secrets, **1 concurrent pipeline**
- **Team:** parallel/matrix, schedules, runner pools, protected secrets, templates
- **Enterprise:** isolated pools, attestation/SBOM, mandatory scanning, HA scheduler

### Deploy
- **Community:** targets, history, rollback, **1 environment**
- **Team:** multi-env lifecycle, gated promotions, rolling/blue-green/canary
- **Enterprise:** multi-site, signed artifacts, attestations, HA controller

## Licensing shape

```text
No licence            → all modules Community
REVIEW-TEAM           → Review Team (others Community)
BUILD-ENTERPRISE      → Build Enterprise
TEAM-BUNDLE           → all five Team
ENTERPRISE-BUNDLE     → all five Enterprise
TEAM-BUNDLE + DEPLOY-ENTERPRISE → additive overlay
```

See [entitlements.md](entitlements.md) for the resolver and offline licence format.
