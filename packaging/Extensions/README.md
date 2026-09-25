# Extension packages

Intended offline commercial / third-party delivery:

```text
signed .module / .fdext package
+
signed forgedeck.lic (when platform entitlements apply)
```

Package document: `module.json` (see `samples/acme-security.module.json` and [module-contract.md](../../docs/architecture/module-contract.md)).

| Check | Meaning |
|-------|---------|
| Package signature | Produced by vendor, unmodified |
| Permission inspection | Installer shows requested platform permissions |
| Runtime licence | Platform or module entitlements |
| Instance binding (optional) | Licence tied to ForgeDeck InstanceId |

```powershell
powershell packaging/Extensions/scan-commercial-package.ps1 -Path path/to/commercial/bin
```
