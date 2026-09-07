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
