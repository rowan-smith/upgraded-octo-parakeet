# Extensions

Every Module and Connector should ship an `extension.json` next to its project folder (copied to build output).

## Manifest identity

Use stable ids such as `forgedeck.review`, `forgedeck.review.enterprise`, `forgedeck.github`. Display names are not identity.

## Triad

```text
Installed  → package / assembly present
Enabled    → extension registry enabled
Licensed   → capability granted by signed entitlement
```

Enterprise features require all three for gated capabilities.

## Capability checks

Prefer `capabilities.Has("Review.MultiApproval")` over `edition == Enterprise`.

## Loading today

`ModuleDiscovery` loads `ForgeDeck.*.dll` that implement `IPlatformModule`, excluding host/core assemblies. Manifest JSON is scaffolding for packaging; production loading still uses assembly discovery plus the Server's project references.

See [module-contract.md](module-contract.md) for the formal package document, `IModuleContext`, provider catalogue, and out-of-process host scaffold.
