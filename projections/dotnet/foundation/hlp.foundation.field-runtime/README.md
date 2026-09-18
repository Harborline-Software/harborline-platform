# Harborline.Foundation.FieldRuntime

The shared field substrate described by DES-0030 and built under T-621. It owns field-kind
registrations, permitted-value domains, narrowing, kind limits and editor selection.
Records owns its grammar and JSON Schema compiler; this producer owns no definition store
and no authoring destination.

## Promotion source

The initial implementation is promoted from `codex/t-615-records` at `a625fdd`:

- `FieldKindRegistry.cs`: exact immutable kind/version lookup and scalar shape admission.
- `RecordsDefinitions.cs`: the three-source `ValueDomainDefinition`, field-kind references,
  scalar shapes, governance defaults, shared constraints and domain-source refusals.
- `RecordsConstraintIntersection.cs`: folding required, multiplicity, access and domains.

Records-specific field traversal, trait-slot binding, identity and reference rules remain
with Records. Substrate refusals use `field.*`, not `records.*`.

## Scope

T-621 adds authority-aware Taxonomy and record-query domain resolution, resolved-membership
narrowing, numeric and byte-size limits, and domain-driven editor selection. Expression
evaluation, record authorization and commit orchestration remain at their existing owners.

Implementation and verification are in progress. This README is not release evidence.

The current executable slice is `ValueDomainAdmission.Validate`: it counts the three
declared sources and refuses anything other than exactly one with
`field.value_domain_source_count`. Its first public-interface test has passed. Dynamic
resolution, kind lookup, limits, intersection and editor selection are not implemented yet.

Records remains in the kernel schema-validation projection. Its eventual binding must
cross a shared contract; the tier fence forbids a kernel-to-foundation assembly reference.
The producer implementation must not weaken that fence to accommodate its consumer.
