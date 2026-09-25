# Commercial protection model

ForgeDeck premium protection is layered. No single layer is sufficient.

```text
SOURCE BOUNDARY     commercial/ is proprietary (unpublished on Community feeds)
LEGAL BOUNDARY      commercial licence (lawyer-reviewed before release)
DISTRIBUTION        private commercial feed / authenticated download / sneaker-net
PACKAGE INTEGRITY   RSA-signed extension envelopes
RUNTIME ENTITLEMENT Ed25519-signed forgedeck.lic per-module tiers → capabilities
INSTANCE BINDING    optional InstanceId / licence-request.nlr challenge
SERVER ENFORCEMENT  capabilities.Has / IEntitlementService on protected APIs
```

## Editions are module entitlements

```text
Community  → no licence file (all modules Community)
Team SKU   → module map with Team levels (bundle or à-la-carte)
Enterprise → module map with Enterprise levels
```

There is no runtime “is this an Enterprise install?” flag outside the licensing subsystem.

## Rules

1. Commercial implementation never lives in Community merely behind a licence check (source boundary).
2. Community never references commercial assemblies (Server may conditionally reference via transitional `IncludeTeam` / `IncludeEnterprise`).
3. Installed ≠ Licensed; Licensed ≠ Installed.
4. Package signature ≠ runtime licence.
5. Private signing keys never ship with the application.
6. Prefer one commercial container image; licence decides entitlements ([entitlements.md](entitlements.md)).

Have the commercial licence text drafted/reviewed by a software-licensing lawyer before public release.
