# ForgeDeck.Runner

Local worker that registers with the ForgeDeck control plane, heartbeats, claims jobs, and executes steps in an isolated workspace.

## Quick start

ForgeDeck.Server listens on **http://localhost:5262** by default.

```bash
dotnet run --project src/ForgeDeck.Runner --launch-profile Local
# first-time registration:
dotnet run --project src/ForgeDeck.Runner --launch-profile Register
# or:
dotnet run --project src/ForgeDeck.Runner -- --url http://localhost:5262 --token <REGISTRATION_TOKEN> --name My-PC
```

### Launch profiles

| Profile     | Project        | Purpose                                           |
|-------------|----------------|---------------------------------------------------|
| `http`      | ForgeDeck.Server   | Default dogfood — real runner mode on :5262       |
| `Simulated` | ForgeDeck.Server   | UI/dev without a local runner process             |
| `Full`      | ForgeDeck.Server   | Git + Review + Pipelines modules enabled          |
| `Local`     | ForgeDeck.Runner | Reuses `.runner/credentials.json` if present      |
| `Register`  | ForgeDeck.Runner | First-time join (replace token in launchSettings) |

In Rider, use compound run config **Platform + Runner** (`.run/`) to start both.

Create a registration token from the UI (**Runners → Add Runner**) or:

```http
POST /api/pipelines/runners/registration-tokens
```

Credentials are stored in `.runner/credentials.json` (token is never logged). Workspaces land under `.runner/work` (`WORK_ROOT` / `--work-root`).

## Real execution vs Simulated

Default Development profile uses `Pipelines:ExecutionMode = Runner`. For UI-only work without a shell runner, launch ForgeDeck.Server with profile **Simulated**.

Pipeline commands execute with the runner process identity — treat registration as trusted infrastructure.

## Job logs

Failed (and live) job output is on the run detail page, with **Open full log** for a dedicated viewer that supports filter, copy, and download.
