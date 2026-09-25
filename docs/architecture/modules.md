# Modules

```text
community/src/Modules/     Community
commercial/team/Modules/   Team deltas (*.Team)
commercial/enterprise/Modules/  Enterprise deltas (*.Enterprise)
```

| Product | Project | Runtime id |
|---------|---------|------------|
| Review | `ForgeDeck.Review` | `review` |
| Review Team | `ForgeDeck.Review.Team` | `review-team` |
| Review Enterprise | `ForgeDeck.Review.Enterprise` | `review-enterprise` |
| Build | `ForgeDeck.Build` | `pipelines` (product name Build) |

Former runtime id `review-commercial` is superseded by `review-team` / `review-enterprise` (config may still list the alias for migration).

Enterprise may depend on Team. Do not duplicate Community Review engines.

First-party modules declare the same package contract as third parties (`module.json` / `ModuleManifest` permissions, events, extension points). See [module-contract.md](module-contract.md).
