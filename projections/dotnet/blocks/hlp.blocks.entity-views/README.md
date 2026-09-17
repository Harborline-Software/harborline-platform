# Harborline.Blocks.EntityViews

The .NET entity-view substrate for Harborline's entity list/detail, containment tree,
condition history, submissions, form bindings, and navigation targets.

It provides:

- **Wire records** matching the Harborline App entity-view payloads, including string timestamps.
- **Ports** for entity reads, previewed writes, condition history, submissions, and form bindings.
- **Renderer-safe helpers** that resolve breadcrumbs without exposing raw entity IDs.
- **Deterministic memory storage** for tests and local composition.
- **Development preview** carrying the pinned Harborline House seed and refusing Production use.

The package owns no HTTP routes, EF persistence, host composition, or renderer. It references only
the frozen identities/forms contracts and is not authorized for public distribution.

## Authored and bound views

The package also owns the platform definition and execution seams for authored views:

- `ViewDefinitionAuthoring` admits typed drafts against the same kind, record-shape, expression,
  measure, widget, action, and transition registries used by runtime composition.
- `IViewDefinitionStore` keeps immutable definition-and-binding snapshots at append-only coordinates;
  publishing selects the highest semantic-version head, while restore creates a new draft.
- `ViewDefinitionPackExporter` exports published system/public definitions with their authored
  bindings and excludes personal definitions.
- `ViewQueryRuntime` checks view-open authority and record shape before data access, then applies
  the Access predicate before authored filters, sorting, grouping, paging, counts, or measures.
- Measures see only current authorized rows and an injected `TimeProvider` instant. Results are
  transient and carry rows, totals, groups, measures, row-action authority, and evaluation time.

Hosts provide persistence, Access policy, record rows, measure implementations, and registries.
Layout owns renderers; this module retains the binding contracts and registered view kinds.
