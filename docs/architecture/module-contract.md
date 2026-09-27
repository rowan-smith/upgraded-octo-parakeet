# Module contract

> **Principle:** first-party Git / Code / Review / Build / Deploy are consumers of the same extension architecture as third parties — not exceptions to it.

## Layers

```text
Platform Core (Auth, RBAC, Projects, Events, Licensing, Registry)
        │
        │  IModuleContext + provider contracts + events
        ▼
 Module host
   ├── InProcessModuleHost     (current first-party path)
   └── OutOfProcessModuleHost  (preferred third-party boundary; not implemented — throws NotSupportedException)
```

## Package document

Ship `module.json` (YAML conceptual equivalent of `module.yaml`). Legacy `extension.json` still loads via `ModulePackageLoader.FromLegacyExtensionJson`.

Fields: metadata, compatibility, runtime (`in-process` | `out-of-process` | `container`), permissions, events, provides/requires, capabilities, extensionPoints, licensing.

`events.publishes` / `events.subscribes` in the package document should match the module’s runtime event contracts (wire type strings). First-party modules also declare the same lists on `ModuleManifest.Publishes` / `Subscribes`.

Sample first-party: `community/src/Modules/Review/module.json`  
Sample third-party: `packaging/Extensions/samples/acme-security.module.json`

## Event registration (in-process)

| Mechanism | Role |
|-----------|------|
| `*.Contracts` + `EventContract<T>` | Stable CLR payload + wire type/version |
| `EventContractRegistration` | Host registers contracts into `IEventRegistry` at startup |
| `AddEventHandler<TEvent,THandler>(consumerId)` | DI registration; activated via `ActivateRegisteredHandlers` (skipped in safe mode) |

Platform-neutral contracts (e.g. `CheckUpdatedEvent`) live in `sdk/ForgeDeck.Contracts`. Module-specific events live in `ForgeDeck.{Module}.Contracts`. Full bus semantics: [events.md](events.md).

## Runtime contracts (`sdk/ForgeDeck.Contracts`)

| Type | Role |
|------|------|
| `IPlatformModule` | In-process registration (DI + endpoints) |
| `IModuleContext` | Capabilities, entitlements, events, provider catalogue |
| `IModuleLifecycle` | Optional Start/Stop |
| `IProviderCatalogue` | “Does something provide Check?” not “Is Build installed?” |
| `IModuleHost` | Start/stop packages (in-proc; OOP throws NotSupportedException) |
| `PlatformPermissions` / `ExtensionPoints` | Stable permission and contribution ids |

## Provider kinds

`Source`, `ChangeSource`, `Check`, `BuildExecution`, `Artifact`, `Secret`, `Deployment`, `Identity`

Bundled modules are the default providers. External Jenkins/SAST/K8s plugins should register the same kinds.

## Isolation policy

| Module class | Hosting |
|--------------|---------|
| First-party ForgeDeck.* | In-process today (performance); still declare permissions/events |
| Third-party | Out-of-process / container via `IModuleHost` (not implemented — throws `NotSupportedException`) |

A third-party crash must not take down the Core process once OOP hosting is fully wired.

## Offline install (target)

```text
signed .module / .fdext
  → verify signature
  → inspect permissions
  → IModuleHost.StartAsync
  → register extensions + providers
```

See also [extensions.md](extensions.md), [entitlements.md](entitlements.md), [feature-matrix.md](feature-matrix.md), [deployment-topology.md](deployment-topology.md).
