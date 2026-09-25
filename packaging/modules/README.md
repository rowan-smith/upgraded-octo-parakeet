# Packaging — modules

Intended artifact shape (not yet automated):

```text
forgedeck-review-1.4.0.fdext
forgedeck-review-enterprise-1.4.0.fdext
forgedeck-build-1.4.0.fdext
```

Each package should contain `extension.json`, server assemblies, frontend assets, migrations, and metadata. Source directories are not copied wholesale into runtime layouts.
