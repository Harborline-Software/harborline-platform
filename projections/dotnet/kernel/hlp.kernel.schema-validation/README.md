# Harborline.Kernel.SchemaValidation

The Dynamic Forms JSON Schema boundary. It registers draft 2020-12 schemas by canonical content, validates UTF-8 JSON payloads, and returns stable field-addressable errors.

This package intentionally excludes schema migration, epoch coordination, compaction, blob storage, and federation. Those legacy registry concerns are not dependencies of Forms validation.

## Records declaration and compilation

`RecordTypeDefinition` carries the Records grammar rather than authored JSON Schema.
`RecordsIntentValidator` reports structural refusals with stable codes and RFC 6901
pointers. `RecordsDefinitionJson` preserves the typed declaration through canonical JSON.

Compositions supply exact `AdmittedFieldKind` revisions through `FieldKindRegistry`.
Each registration names its scalar value shape and optional governance defaults.
Unknown kinds and versions refuse compilation; a kind's name never selects its JSON type.

`RecordsDefinitionCompiler.CompileSchema` admits and compiles a declaration without
registering anything. `CompileAndRegisterAsync` registers the same schema text through
`ISchemaRegistry`. Static compilation currently covers scalar shapes, literal domains,
text patterns, string-length parameters, required fields, multiplicity and translation maps.
`RecordsReferenceAdmission` supplies the shared target predicate for picker and write callers.

`RecordsFieldKindDefaultMaterializer` copies creation defaults with kind/version provenance
and preserves materialized author edits. A composing creation path must call it explicitly;
compilation and publication do not silently supply defaults.

## Boundaries and unfinished integration

The module contains no Records definition lifecycle store. T-620 owns the shared versioned
store in builder-definitions. Records binding to that store, including publication, restore,
history and production head resolution, remains owed; canonical round-trip tests do not
prove those lifecycle operations.

Rule conditions, dynamic Taxonomy/query domain membership and policy enforcement require
their owning interpreters. Their representation in the grammar is not evidence of runtime
execution. The static compiler is not a complete Records write pipeline or field runtime.
