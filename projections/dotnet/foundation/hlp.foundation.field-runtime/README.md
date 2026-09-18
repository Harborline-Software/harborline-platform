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

Numeric limits use the two explicit kind-parameter names `total_digits` and
`fraction_digits`, as ruled in T-621 on 2026-09-18. Leading zeroes and the sign do not count
toward `total_digits`; trailing zeroes after the point do. `fraction_digits` counts digits
after the point, and cannot exceed a declared `total_digits`. Both refuse overflow without
rounding. Schema compilation must carry `fraction_digits` as `multipleOf` and retain an
executable `total_digits` validator. These are implementation requirements, not yet proof
that the runtime enforces them.

Implementation and verification are in progress. This README is not release evidence.

The source now includes typed and raw JSON domain admission, exact kind lookup, and
`IFieldKindRuntime.Bind`, which pairs a compiled schema with its complete value validator.
Limits include the two digit facets, inclusive numeric bounds, Unicode scalar length and
UTF-8 byte size. Refusals preserve the authored or submitted RFC 6901 location.

Only the initial source-count test has run successfully. The new admission, binding and
limit code is unverified while test restore is blocked. Dynamic domain resolution,
constraint intersection, editor selection and the Records consumer binding remain work
to complete; exposing the shared contract is not proof that Records already uses it.

Records remains in the kernel schema-validation projection. Shared declarations live in
`Harborline.Contracts.Fields`, contributed to the existing `Harborline.Contracts` assembly.
The interpreter stays here. Records' eventual binding crosses shared contracts; the tier
fence still forbids a kernel-to-foundation assembly reference. This does not add a library
or an exemption to that fence.
