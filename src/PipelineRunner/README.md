# PipelineRunner

Local worker that registers with the ForgeDeck control plane, heartbeats, claims jobs, and executes steps in an isolated workspace.

## Quick start

Platform.Api listens on **http://localhost:5262** by default.

```bash
dotnet run --project src/PipelineRunner --launch-profile Local
# first-time registration:
dotnet run --project src/PipelineRunner --launch-profile Register
# or:
dotnet run --project src/PipelineRunner -- --url http://localhost:5262 --token <REGISTRATION_TOKEN> --name My-PC
```

### Launch profiles

| Profile | Project | Purpose |
|---------|---------|---------|
| `http` | Platform.Api | Default dogfood — real runner mode on :5262 |
| `Simulated` | Platform.Api | UI/dev without a local runner process |
| `Full` | Platform.Api | Git + Review + Pipelines modules enabled |
| `Local` | PipelineRunner | Reuses `.runner/credentials.json` if present |
| `Register` | PipelineRunner | First-time join (replace token in launchSettings) |

In Rider, use compound run config **Platform + Runner** (`.run/`) to start both.

Create a registration token from the UI (**Runners → Add Runner**) or:

```http
POST /api/pipelines/runners/registration-tokens
```

Credentials are stored in `.runner/credentials.json` (token is never logged). Workspaces land under `.runner/work` (`WORK_ROOT` / `--work-root`).

## Real execution vs Simulated

Default Development profile uses `Pipelines:ExecutionMode = Runner`. For UI-only work without a shell runner, launch Platform.Api with profile **Simulated**.

Pipeline commands execute with the runner process identity — treat registration as trusted infrastructure.

## Job logs

Failed (and live) job output is on the run detail page, with **Open full log** for a dedicated viewer that supports filter, copy, and download.
