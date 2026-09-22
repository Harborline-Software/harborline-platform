# @harborline-software/rule-authoring

The calculation authoring bridge over `@harborline-software/rule-engine`
(`hlp.foundation.rule-authoring`, ADR 0146 D2/D5) — the TypeScript projection of the
Harborline rules authoring lane.

It provides:

- **Drafts** (`DecisionTableDraft` / `FormulaDraft`) — the shapes editors hold and persist.
- **Lowering** (`compile.ts`) — drafts onto the engine's authoring skins
  (`DecisionTableSkin` / `FormulaSkin`), compiled to a plain `RuleDefinition` by the shipped
  skin compilers, plus `evaluatePreview` (probe-derived fired-row + D10 trace).
- **Lint** (`lint.ts`) — advisory authoring-time findings (`rules.lint.*`); the load-bearing
  rejection stays with the skin compiler.
- **Seeds** (`seeds.ts`) — predefined-first blank drafts for the "New" flow.
- **Typed source and intent** (`RuleDefinitionDocument`, strict codec and validator) — Author,
  Publish and Persisted diagnostics share the full compiler, stable codes and RFC 6901 locations.
- **Key suggestion** — a pure advisory slug without a version suffix; it never allocates a key.

The .NET `RuleDefinitionCatalog` in blocks.builder-definitions composes the shared versioned
store and archive lifecycle. Foundation has no store or dependency on blocks. Callers supply
version identities, labels, request ids and revision fences. `VersionPolicy` selects a version
at resolution; it is not part of authored source. TS intent success and preview never claim
that a published version was committed.

The bridge OWNS NO evaluator semantics: hit-policy, no-match, reified bounds, and
undeclared-ref enforcement live in the rule engine, the single source of truth for
rejection and evaluation.

This package is an internal-only projection (`private: true`); public distribution
is not authorized.
