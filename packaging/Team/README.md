# Team distribution

Team packages are proprietary compiled extensions (e.g. `forgedeck-review-team.fdext`).

```bash
dotnet build commercial/team/Modules/Review.Team/ForgeDeck.Review.Team/ForgeDeck.Review.Team.csproj -c Release
dotnet build community/src/Server/ForgeDeck.Server/ForgeDeck.Server.csproj -c Release -p:IncludeTeam=true
```

Distribute via a private commercial feed. Package possession does not grant entitlement — a signed `forgedeck.lic` is still required.
