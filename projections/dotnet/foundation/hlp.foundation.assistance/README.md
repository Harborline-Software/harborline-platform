# Harborline Foundation Assistance

Owns the platform-neutral Assistance definition contract: canonical JSON, phase-aware command and safety-floor admission, and provider-neutral pack export under the shared builder-definitions `DefinitionKind.Pilot` namespace. It owns no command catalogue, definition store, runtime proposal evaluation, or model execution.

At Author, Publish and Install, the injected `ICommandCatalogueRegistry` supplies each command's exporting package, export status, tier floor, argument schema and permitted recipients. A binding may only narrow these entries; binding-wide recipients must be permitted by every referenced command. T-612 supplies the released authority-artifact adapter; this foundation package has no blocks dependency.

`AssistanceArgsSchemaNarrowing` provides a sufficient inclusion proof for required names, enum subsets, numeric/string/array bounds, properties, items and additionalProperties. Type and pattern changes refuse; unsupported keywords must remain structurally equal. Missing baseline constraints refuse. The same checker protects previous versions. Schemas are bounded to 65536 JSON characters each, 32 nested comparisons and 4096 comparison steps; exceeding a bound refuses. This is intentionally conservative, not a general JSON Schema inclusion solver.
