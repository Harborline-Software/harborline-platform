# Harborline.Kernel.SchemaValidation

The Dynamic Forms JSON Schema boundary. It registers draft 2020-12 schemas by canonical content, validates UTF-8 JSON payloads, and returns stable field-addressable errors.

This package intentionally excludes schema migration, epoch coordination, compaction, blob storage, and federation. Those legacy registry concerns are not dependencies of Forms validation.

## Records declaration and compilation

`RecordTypeDefinition` carries the Records grammar rather than authored JSON Schema.
`RecordsIntentValidator` reports structural refusals with stable codes and RFC 6901
pointers. `RecordsDefinitionJson` preserves the typed declaration through canonical JSON.

Compositions inject the shared `IFieldKindRuntime`, backed by the foundation
`FieldKindRuntime` and its exact immutable registrations. The kernel does not own a
field-kind registry or guess built-in kinds. Unknown kinds, versions and invalid
parameters retain their `field.*` refusals and authored pointers.

`RecordsDefinitionCompiler.CompileSchema` admits and compiles a declaration without
registering anything. `CompileAndRegisterAsync` registers the same schema text through
`ISchemaRegistry`. Each scalar begins as a detached copy of the bound
`ICompiledFieldKind.JsonSchema`, including exact kind metadata and every parameter.
Records then overlays only its literal domain, pattern, requiredness, multiplicity and
translation grammar. The registry used by composition receives the same field-kind runtime
so runtime-only limits execute at validation time.
`RecordsReferenceAdmission` supplies the shared target predicate for picker and write callers.

`RecordsFieldKindDefaultMaterializer` binds every declared kind before copying creation
defaults with kind/version provenance. It preserves materialized author edits and old
provenance, while a later unresolved kind or invalid parameter still refuses. A composing
creation path must call it explicitly; compilation and publication do not silently supply
defaults.

## Boundaries and unfinished integration

The module contains no Records definition lifecycle store. T-620 owns the shared versioned
store in builder-definitions. Records binding to that store, including publication, restore,
history and production head resolution, remains owed; canonical round-trip tests do not
prove those lifecycle operations.

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
