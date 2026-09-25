# Community distribution

```bash
dotnet publish community/src/Server/ForgeDeck.Server/ForgeDeck.Server.csproj -c Release -o dist/server
dotnet publish community/src/Runner/ForgeDeck.Runner/ForgeDeck.Runner.csproj -c Release -o dist/runner
```

Community builds must not require `commercial/`.
