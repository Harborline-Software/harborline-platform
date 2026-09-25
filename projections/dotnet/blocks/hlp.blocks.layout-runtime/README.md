# Harborline Blocks Layout Runtime

The projection-neutral renderer plan for published Layout definitions.

## Text runs and the fallback literal

A `text` binding composes its literal and record-field runs in order (layout-ck-43). A field run renders `fallback` only when its resolved value is null or missing. An empty string (`""`) is present and renders as an empty string; it does not trigger the fallback. A denied read follows the missing-value rendering path without exposing the denial to the viewer. (layout-ck-44, T-724 ruling 71)
