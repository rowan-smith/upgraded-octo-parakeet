# Docker

| File | Purpose |
|------|---------|
| `Dockerfile` | Community-only image (public / FOSS builds) |
| `Dockerfile.commercial` | Full commercial image — identical binaries; licence decides entitlements |
| `compose.yaml` | **appliance** profile (single box) |
| `compose.team-split.yaml` | **team-split** overlay (Core/SCM + Build + Deploy hosts) |

Topology vs licensing: [deployment-topology.md](../../docs/architecture/deployment-topology.md).

```bash
docker build -f Dockerfile.commercial -t forgedeck/commercial:latest .
docker compose up --build
docker compose -f compose.yaml -f compose.team-split.yaml --profile team-split up --build
```
