# Packaging — server

Build the shared Server host:

```bash
dotnet publish src/ce/server/ForgeDeck.Server/ForgeDeck.Server.csproj -c Release -o dist/server
```

Community publish omits Enterprise packages. Enterprise:

```bash
dotnet publish src/ce/server/ForgeDeck.Server/ForgeDeck.Server.csproj -c Release -o dist/server -p:IncludeEE=true
```

There is one Server binary for both editions. Do not create `ForgeDeck.Server.Enterprise`.
