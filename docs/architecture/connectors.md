# Connectors

Connectors integrate external services. Modules provide product functionality.

```text
community/src/Connectors/     Community
commercial/*/Connectors/      Team / Enterprise (when present)
```

Connectors are installed from Settings alongside Modules. They are **not** product Modules and do **not** participate in first-run Core onboarding.

## Rule

| Kind | Example | Role |
|------|---------|------|
| Connector | GitHub | Implements `ISourceProvider` / `IChangeProvider` |
| Module | Review, Code, Build | Product behaviour consuming those providers |

Connectors must not become dumping grounds for module business rules. Modules must not reference connector implementation projects — only shared contracts.

## Current

| Connector | Project | Edition |
|-----------|---------|---------|
| GitHub | `ForgeDeck.Connector.GitHub` | CE |

Namespace: `ForgeDeck.Connectors.GitHub`. Assembly name: `ForgeDeck.Connector.GitHub`.
