# Harborline Blocks Layout Runtime

The projection-neutral renderer plan for published Layout definitions and one bounded
measure-observe binding adapter.

`LayoutMeasureBindingResolver` places a caller-selected observe block. Its caller must supply the
surface default intent explicitly; the adapter uses the block intent when present and otherwise
inherits that surface default. For an effective observe intent, it passes the measure name, the
caller's trusted `MeasureRequest`, cancellation token, and RFC 6901 binding pointer to the shared
`IMeasureCatalogue` evaluation path. It returns the catalogue's typed `MeasureResult` unchanged
with Layout's block identity, or one stable Layout refusal retaining the catalogue's cause code
and pointer.

The adapter owns only Layout binding identity and placement. It does not fetch or enumerate caller
rows, infer a basis, supply tenant/principal/clock defaults, filter Access, narrow authored filters,
dispatch on measure entry kind, recompute aggregation, or retry a refusal. Field, query, template,
and static execution remain outside this adapter.
