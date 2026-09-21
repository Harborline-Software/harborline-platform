# Harborline.Kernel.SchemaValidation

The Dynamic Forms JSON Schema boundary. It registers draft 2020-12 schemas by canonical content, validates UTF-8 JSON payloads, and returns stable field-addressable errors.

This package intentionally excludes schema migration, epoch coordination, compaction, blob storage, and federation. Those legacy registry concerns are not dependencies of Forms validation.

## Records declaration and compilation

`RecordTypeDefinition` carries the Records grammar rather than authored JSON Schema.
`RecordsIntentValidator.ValidateAsync` and `ValidateJsonAsync` require an explicit
`FieldDomainScope` and cancellation token. They detach the authored declaration, bind exact
field kinds, and await shared narrowing/intersection proofs before admitting publication.
Every proof in one admission must use the same source snapshot revision. Structural and
shared `field.*` refusals retain stable RFC 6901 pointers. `RecordsDefinitionJson` preserves
the typed declaration through canonical JSON. Its admission serializer retains nulls, and its
pure raw-shape check applies the same CLR grammar before deserialization without opening a domain
source or mutating the schema registry. This prevents invalid collection nulls and numeric enum
wire values from being omitted or normalized before admission.

Type-level unique constraints are declaration-time identity metadata. Each constraint has a
nonblank, ordinal, record-type-scoped identity and one or more declared stable field keys; composite
constraints may span fields whose display names are identical. Admission aggregates duplicate
constraint identities and blank, unresolved, ambiguous or repeated field-key refusals before shared
domain access or registry mutation. DES-0015 `records-ck-32` also applies to every participating
field: an identity field may never be translatable. This module does not enforce uniqueness across
stored records; atomic record-set enforcement remains the Records write interpreter's responsibility.

Compositions inject the shared `IFieldKindRuntime`, backed by the foundation
`FieldKindRuntime` and its exact immutable registrations. The kernel does not own a
field-kind registry or guess built-in kinds. Unknown kinds, versions and invalid
parameters retain their `field.*` refusals and authored pointers.

`RecordsDefinitionCompiler.CompileSchemaAsync` admits and compiles a declaration without
registering anything. `CompileAndRegisterAsync` registers the same admitted schema text through
`ISchemaRegistry`. `CompileAndRegisterResultAsync` additionally returns the detached definition,
policies and exact field-kind bindings from that same admission with the registered schema, so a
consumer does not perform a second binding pass. Each scalar begins as a detached copy of the bound
`ICompiledFieldKind.JsonSchema`, including exact kind metadata and every parameter.
Records then overlays authored literal contributions as `enum`/`allOf` constraints plus its
pattern, proven requiredness, multiplicity and translation grammar. Authority-filtered proof
values never become static schema membership, so schema identity is stable across principals.
Dynamic Taxonomy/query declarations remain authored for runtime enforcement. The registry used
by composition receives the same field-kind runtime so runtime-only limits execute at validation time.
`RecordsReferenceAdmission` supplies the shared target predicate for picker and write callers.

`RecordsFieldKindDefaultMaterializer` binds every declared kind before copying creation
defaults with kind/version provenance. It preserves materialized author edits and old
provenance, while a later unresolved kind or invalid parameter still refuses. A composing
creation path must call it explicitly; compilation and publication do not silently supply
defaults.

## Boundaries and unfinished integration

The module contains no Records definition lifecycle store. T-620 owns the shared versioned
store in builder-definitions, whose Records adapter composes this grammar, validator and compiler.
Publication, restore, history and production head resolution remain builder-definitions concerns;
canonical round-trip tests in this module alone do not prove those lifecycle operations.

Rule conditions, dynamic Taxonomy/query domain membership and policy enforcement require
their owning interpreters. Their representation in the grammar is not evidence of runtime
execution. The static compiler is not a complete Records write pipeline or field runtime.

## Shared field-kind validation

A required `IFieldKindRuntime` constructor dependency binds `x-harborline-field-kind`
metadata through shared Contracts. The field-runtime producer still owns every kind limit;
the kernel has no foundation reference or duplicate limit implementation. The exact kind
identity, version and parameters contribute to the schema's content address. A binding
without its runtime, or with malformed or unresolved metadata, refuses registration.

Evaluation passes the original JSON value and instance pointer to the bound validator.
All `field.*` refusals survive, including simultaneous digit overflows. JSON Schema's own
applicators handle nested properties, arrays, references and alternatives. Valid branches
do not contribute errors from unsuccessful alternatives.
