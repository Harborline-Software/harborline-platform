# Harborline.Kernel.SchemaValidation

The Dynamic Forms JSON Schema boundary. It registers draft 2020-12 schemas by canonical content, validates UTF-8 JSON payloads, and returns stable field-addressable errors.

This package intentionally excludes schema migration, epoch coordination, compaction, blob storage, and federation. Those legacy registry concerns are not dependencies of Forms validation.

An optional `IFieldKindRuntime` constructor dependency binds `x-harborline-field-kind`
metadata through shared Contracts. The field-runtime producer still owns every kind limit;
the kernel has no foundation reference or duplicate limit implementation. The exact kind
identity, version and parameters contribute to the schema's content address. A binding
without its runtime, or with malformed or unresolved metadata, refuses registration.

Evaluation passes the original JSON value and instance pointer to the bound validator.
All `field.*` refusals survive, including simultaneous digit overflows. JSON Schema's own
applicators handle nested properties, arrays, references and alternatives. Valid branches
do not contribute errors from unsuccessful alternatives.
