# DI lifetimes

ForgeDeck modules commonly register application services and stores as **singletons** that open short-lived EF contexts via `IDbContextFactory<T>`:

```csharp
services.AddDbContextFactory<ReviewDbContext>(...);
services.AddSingleton<IChangeRepository, EfChangeRepository>();
```

## Why

- Module hosts are long-lived; stores are thin façades over SQLite.
- Factory-created contexts avoid captive `DbContext` instances across requests.
- Matches the appliance model (single process, shared in-memory caches where intentional).

## Rules of thumb

1. Never inject a scoped `DbContext` into a singleton — always use `IDbContextFactory<T>` (or create/dispose per operation).
2. Keep mutable per-request state out of singletons (use `HttpContext` / scoped services).
3. Prefer scoped services for new request-bound workflows when they do not need process-wide caching.
4. Background hosted services that touch stores must tolerate concurrent callers (locks or SQLite write serialization).

Architecture tests do not currently assert lifetimes; treat this document as the contract for new module code.
