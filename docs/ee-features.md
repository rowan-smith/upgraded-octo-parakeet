# Enterprise / commercial development

Proprietary code lives under `commercial/` (Team + Enterprise).

```bash
dotnet run --project community/src/Server/ForgeDeck.Server -p:IncludeTeam=true
dotnet run --project community/src/Server/ForgeDeck.Server -p:IncludeEnterprise=true
```

See [architecture/commercial-protection.md](architecture/commercial-protection.md) and [LICENSE.md](../LICENSE.md).

Have commercial licence terms reviewed by a software-licensing lawyer before release.
