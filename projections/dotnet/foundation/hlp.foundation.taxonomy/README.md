# Harborline Foundation Taxonomy

Owns portable Taxonomy definition contracts, canonical JSON, phase-aware structural admission, and
the dependency-free traversal interpreter (`TaxonomyInterpreter`): pinned-definition and
classification resolution, ancestors/descendants, subsumption, succession, whole-scheme expansion,
and definition diff. It owns no catalogue adapter, pack transport, or authoring UI.

`CaptureCodingAnswers(tenant, bindings, queries, window)` is an explicit finite capture outside
Rules evaluation. Each system alias pins exact tenant/definition/version coordinates and the
lowercase SHA-256 of `TaxonomyDefinitionJson.SerializeCanonical`. The producer uses one canonical
buffer per selected definition for hash and owned clone, applies Install admission, validates all
selected parent paths with `GetAncestors`, and delegates each question to `Subsumes`. It emits the
ordered `harborline-taxonomy-coding-answers/1` tuple document with one trailing newline.

Bindings and queries must be nonempty and unique. Selected nodes and requests are each bounded
at 5000 before traversal; the output is capped while writing at 262144 UTF-8 bytes and 5000 JSON
values (containers included), within depth 64. Unknown codes in a complete admitted definition
produce false. Missing definitions, wrong pins, malformed requests and overflow refuse with
`TaxonomyCodingCaptureException.Code`; existing admission/traversal refusals propagate. Capture
supplies subsumption answers only; it does not expand a permitted-value domain or walk overlays.
