# Harborline.Foundation.FieldRuntime

The shared field substrate described by DES-0030 and built under T-621. It owns field-kind
registrations, permitted-value domains, narrowing, kind limits and editor selection.
Records keeps its grammar and compiler. This producer owns no definition store or authoring
destination.

## Promotion and boundary

Promoted from `codex/t-615-records` at `a625fdd`: the field-kind registry, three-source domain
declarations and constraint intersection. Shared declarations now live in
`Harborline.Contracts.Fields`, contributed to the existing `Harborline.Contracts` assembly.
Substrate refusals use `field.*`, not `records.*`. The kernel consumes shared contracts; it
does not reference this foundation assembly.

Expression evaluation remains with Rules. Record authorization, definition persistence and
commit orchestration remain with their existing owners. Records integration resumes in T-615.

## Executable contract

- `IFieldKindRuntime.Bind` resolves an exact admitted kind/version and compiles its parameters.
  The resulting schema retains all parameters in `x-harborline-field-kind`; schema registration
  requires the runtime and binds the executable validator. Standard keywords alone cannot
  enforce every limit.
- `IFieldDomainRuntime.ResolveAsync` resolves exactly one literal set, exact Taxonomy scheme
  revision or predicate-bearing record query. The host supplies a complete pinned tenant
  snapshot and per-member read authority. Returned membership and editor cardinality contain
  only values the authenticated caller may read.
- `IntersectAsync` and `NarrowAsync` prove constraints against complete membership, including
  members withheld from the caller. A consumer cannot widen a domain, drop required, loosen
  multiplicity or relax access. Proofs retain source and predicate attribution.
- `Validate` enforces required, multiplicity and readable membership, then applies the compiled
  kind to each original scalar. Undefined/null is absent; an array carries repeated values.
  Refusals carry stable codes and RFC 6901 pointers without changing the candidate.

Kind parameters include Unicode-scalar `min_length`/`max_length`, exact inclusive
`minimum`/`maximum`, UTF-8 `max_bytes`, `total_digits` and `fraction_digits`.
Leading zeroes and sign do not count toward total digits; trailing decimal zeroes do.
Fraction digits cannot exceed declared total digits. Overflow refuses, never rounds.
Fraction digits contributes JSON Schema `multipleOf = 10^-n`; total digits remains executable.

The runtime chooses the editor after authority filtering: zero values gives None, one gives
SingleValue, two through five gives RadioGroup, and larger domains give ChoiceList,
TaxonomyPicker or RecordPicker according to source. Five is implementation policy, not an
owner ruling. Forms never consults an authored control hint for this choice. React and Blazor
render the returned choice; pickers search the supplied pinned membership locally and display
at most 25 matches. Query text is not a submitted value.

## Consumer composition

Forms resolves exact tenant/schema field bindings for render, validation and publication.
Submission checks the accepted/pruned candidate through the shared validator before persistence.
Configured publication requires all binding/runtime ports, the authenticated caller and the
matching active tenant; publication authorization remains host-owned. Views exposes the same
resolved column domains. An independent test host composes the shared runtime at DataExchange's
existing protected-payload port; this does not ship or change a DataExchange adapter.

## Verification status

After integrating main at `b061140`, all six whole test projects passed: FieldRuntime 165,
SchemaValidation 44, Forms.Engine 198, EntityViews 68, Blazor 707 and UI support 35, with zero
failures or skips. React SchemaForm, FormView, RadioGroup and SelectField passed 99 tests;
the React build and typecheck passed. Twelve neutral UI cases also executed individually
before the merge. The publication inventory and picker-keyboard tooling tests passed.

These focused results are not the platform gate. Packed-library probes and the full platform
gate remain required before landing.
