# Universal Event Architecture

Cross-module integration uses domain events. Modules stay independently optional: publishers and subscribers never share implementation assemblies.

## Ownership

| Layer | Owns |
|-------|------|
| **Core** (`ForgeDeck.Messaging`) | Envelope, registry, publisher/bus, outbox/inbox, retry, diagnostics |
| **Modules** | Event *semantics* — CLR payloads, contract type strings, handlers, `module.json` publishes/subscribes |

Core never interprets module-specific fields. Modules never implement their own bus.

## Naming convention

Prefer CLR names of the form **`ModuleSemanticEvent`**:

```text
GitRepositoryPushEvent
ReviewRequestedEvent
BuildPipelineRunStartedEvent
DeploySucceededEvent
CheckUpdatedEvent
```

Wire type strings are reverse-DNS (`forgedeck.git.repository-push`, `forgedeck.review.requested`, …). This naming is **convention only** — it is not enforced by the compiler or registry.

## Envelope and actor

Every published message is wrapped in `EventEnvelope` / `EventEnvelope<TEvent>` (`sdk/ForgeDeck.Contracts/Events`):

| Field | Role |
|-------|------|
| `Id` | Unique event id |
| `Type` / `Version` | Registered contract identity |
| `OccurredAt` | UTC timestamp |
| `Actor` | `EventActor` (`User` / `System` / `Extension` / `External`) |
| `CorrelationId` | Shared id for a causal chain |
| `CausationId` | Parent event id (if any) |
| `OrganisationId` / `ProjectId` | Optional tenancy scope |
| `Publisher` | Logical publisher name |
| `Data` | Minimal durable JSON payload |

Do not pass module implementation objects through events — only stable DTO payloads.

## Correlation and causation

- First event in a chain: `CorrelationId == Id`, `CausationId == null`.
- Nested publish (handler → new event): child inherits the parent’s `CorrelationId` and sets `CausationId` to the parent’s `Id` via `CorrelationContext`.
- Diagnostics: `GET /api/platform/events/trace/{correlationId}`.

## Outbox, inbox, and delivery

Publish path (durable, default):

1. Serialize payload + envelope.
2. Enqueue to **outbox** (same DB connection as platform when configured).
3. Background dispatcher claims pending rows and calls the bus.
4. Bus fans out to active subscriptions; each consumer gets an **inbox** delivery row.
5. Success → processed; failure → backoff retry then dead-letter.

Delivery is **at-least-once**. Handlers must be idempotent (use event id / consumer id / domain keys). Missing subscribers are a no-op — publish still succeeds.

## Event vs query

| | Events | Queries / providers |
|--|--------|---------------------|
| Purpose | Notify that something happened | Read current state |
| Coupling | Fire-and-forget; optional consumers | Caller depends on a provider interface |
| Example | `GitRepositoryPushEvent` | `IGitService`, `ICheckProvider` |
| Absence | Publisher unaffected if Build is off | Caller must handle missing provider |

Do not use events as a request/response RPC. Prefer provider interfaces (`IProviderCatalogue`) for synchronous reads.

## Package layout

```text
sdk/ForgeDeck.Contracts/
  Events/          # IEventPublisher, IEventHandler, EventEnvelope, registry APIs
  Checks/          # CheckUpdatedEvent (provider-neutral platform contract)

community/src/Core/ForgeDeck.Messaging/
  Bus/ Outbox/ Inbox/ Dispatch/ Diagnostics/ Publishing/ Registry/
                   # Infrastructure only — no module semantics

community/src/Modules/Git/ForgeDeck.Git.Contracts/Events/
community/src/Modules/Review/ForgeDeck.Review.Contracts/Events/
community/src/Modules/Build/ForgeDeck.Build.Contracts/Events/
community/src/Modules/Deploy/ForgeDeck.Deploy.Contracts/Events/
                   # Module DTOs + EventContract<T> registrations

community/src/Modules/*/ForgeDeck.*/   # Handlers via AddEventHandler<TEvent,THandler>
```

Implementation assemblies (`ForgeDeck.Review`, `ForgeDeck.Build`, …) may reference peer **`*.Contracts`** only — never each other’s implementation projects. See [dependency-rules.md](dependency-rules.md).

## Registration

1. Module exposes `EventContractRegistration` and `ModuleManifest.Publishes` / `Subscribes`.
2. Host registers contracts from DI after build.
3. `ActivateRegisteredHandlers` wires `EventHandlerRegistration` entries (skipped in safe mode).
4. Package `module.json` `events.publishes` / `events.subscribes` documents the same intent for OOP / third-party modules.

## Safe mode and optionality

`FORGEDECK_SAFE_MODE=true` or config `SafeMode: true` starts Core **without** discovering modules or activating handlers. Messaging infrastructure and `/api/platform/events/*` diagnostics remain available. Disabling a single module (`Modules:{id}:Enabled=false`) drops its handlers; other modules can still publish.

## Diagnostics

| Route | Purpose |
|-------|---------|
| `GET /api/platform/events` | Recent outbox, subscriptions, backlog, failures |
| `GET /api/platform/events/outbox` | Recent outbox rows |
| `GET /api/platform/events/failures` | Failed deliveries |
| `GET /api/platform/events/subscriptions` | Active consumers |
| `GET /api/platform/events/trace/{correlationId}` | Correlation graph |
| `POST .../deliveries/{eventId}/{consumerId}/retry` | Re-queue |
| `POST .../deliveries/{eventId}/{consumerId}/acknowledge` | Manual ack |

Requires `modules.manage` or `audit.read`.

See also [module-contract.md](module-contract.md), [dependency-rules.md](dependency-rules.md).
