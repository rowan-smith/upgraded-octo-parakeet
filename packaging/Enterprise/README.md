# Enterprise distribution

Enterprise = Community + Team + Enterprise packages.

```bash
dotnet build community/src/Server/ForgeDeck.Server/ForgeDeck.Server.csproj -c Release -p:IncludeEnterprise=true
```

Official commercial packages must be signed. Scan outputs with `packaging/Extensions/scan-commercial-package.ps1` before publish.
