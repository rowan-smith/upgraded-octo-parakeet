# Packaging — runner

```bash
dotnet publish src/ce/runner/ForgeDeck.Runner/ForgeDeck.Runner.csproj -c Release -o dist/runner
```

Prefer one CE runner with optional Enterprise extensions later. Advanced scheduling should stay server-side when possible.
