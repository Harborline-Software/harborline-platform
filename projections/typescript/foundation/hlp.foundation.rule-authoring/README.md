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
- **Catalog** (`RuleCatalog` over the `RuleCatalogStore` port) — the named-rule registry:
  create / draft / append-only monotonic published versions (S-8 watermark, downgrade
  refusal) / duplicate / archive-not-delete, with persistence injected behind the store port.
- **Admission fence** (`publishRule`) — compile-first, fail-closed publish carrying stable
  `rule.skin.*` rejection codes; every publish is a control change.

The bridge OWNS NO evaluator semantics: hit-policy, no-match, reified bounds, and
undeclared-ref enforcement live in the rule engine, the single source of truth for
rejection and evaluation.

This package is an internal-only projection (`private: true`); public distribution
is not authorized.
