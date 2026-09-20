# Harborline.Foundation.RuleAuthoring

The calculation authoring bridge over the shipped rule engine (`hlp.foundation.rule-authoring`,
ADR 0146 D2/D5) — the .NET twin of the TypeScript `@harborline-software/rule-authoring` package.

It provides:

- **Drafts** (`DecisionTableDraft` / `FormulaDraft`) — the shapes editors hold and persist.
- **Lowering** (`SkinLowering`) — drafts onto the engine's authoring skins
  (`DecisionTableSkin` / `FormulaSkin`), compiled to a plain `RuleDefinition` by the shipped
  skin compilers, plus the live-preview evaluation (probe-derived fired-row + D10 trace).
- **Lint** (`RuleLint`) — advisory authoring-time findings (`rules.lint.*`); the load-bearing
  rejection stays with the skin compiler.
- **Seeds** (`RuleSeeds`) — predefined-first blank drafts for the "New" flow.
- **Typed source and intent** (`RuleDefinitionDocument`, strict codec and validator) — Author,
  Publish and Persisted diagnostics share the full compiler, stable codes and RFC 6901 locations.
- **Key suggestion** — a pure advisory slug without a version suffix; it never allocates a key.

The .NET `RuleDefinitionCatalog` in blocks.builder-definitions composes the shared versioned
store and archive lifecycle. Foundation has no store or dependency on blocks. Callers supply
version identities, labels, request ids and revision fences. `VersionPolicy` selects a version
at resolution; it is not part of authored source. TS intent success and preview never claim
that a published version was committed.

The bridge OWNS NO evaluator semantics: hit-policy, no-match, reified bounds, and
undeclared-ref enforcement live in `Harborline.Foundation.RuleEngine`, the single source of
truth for rejection and evaluation.

This package is an internal-only projection; public distribution is not authorized
(`HarborlinePublicDistributionAuthorized=false`).
