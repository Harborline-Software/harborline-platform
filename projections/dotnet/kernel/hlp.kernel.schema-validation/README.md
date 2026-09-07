# Harborline.Kernel.SchemaValidation

The Dynamic Forms JSON Schema boundary. It registers draft 2020-12 schemas by canonical content, validates UTF-8 JSON payloads, and returns stable field-addressable errors.

This package intentionally excludes schema migration, epoch coordination, compaction, blob storage, and federation. Those legacy registry concerns are not dependencies of Forms validation.
