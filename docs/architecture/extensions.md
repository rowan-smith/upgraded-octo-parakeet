# Extensions

Every Module and Connector should ship an `extension.json` next to its project folder (copied to build output). Prefer `module.json` for Modules; legacy `extension.json` still loads via the package loader.

## Manifest identity

Use stable ids such as `forgedeck.review`, `forgedeck.review.enterprise`, `forgedeck.github`, `forgedeck.code`. Display names are not identity.

## Lifecycle axes

```text
Installed   → package / assembly present
Enabled     → extension registry enabled (server-wide)
Licensed    → capability granted by signed entitlement
Configured  → Module-specific setup exists where needed (often per Project)
```

Enterprise features require Installed ∧ Enabled ∧ Licensed for gated capabilities.

### Server vs Project

```text
Module installed + enabled on server
        ↓
Module available to all Projects
```

A Project may then be **configured** or **not configured** for that Module (for example Build pipelines defined for one Project but not another). There is **no** separate per-project enablement state for now. Avoid stacking:

```text
installed globally
enabled globally
enabled for Project
configured for Project
licensed
```

Per-project enablement can be added later if an isolation requirement appears.

## Capability checks

Prefer `capabilities.Has("Review.MultiApproval")` over `edition == Enterprise`.

## Loading today

`ModuleDiscovery` loads `ForgeDeck.*.dll` that implement `IPlatformModule`, excluding host/core assemblies. Manifest JSON is scaffolding for packaging; production loading still uses assembly discovery plus the Server's project references.

See [module-contract.md](module-contract.md) for the formal package document, `IModuleContext`, provider catalogue, and out-of-process host boundary (not implemented yet).
