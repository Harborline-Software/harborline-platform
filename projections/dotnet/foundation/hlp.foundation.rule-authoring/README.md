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
- **Catalog** (`RuleCatalog` over `IRuleCatalogStore`) — the named-rule registry: create /
  draft / append-only monotonic published versions (S-8 watermark, downgrade refusal) /
  duplicate / archive-not-delete, with persistence injected behind the store port.
- **Admission fence** (`PublishAdmission.PublishRuleAsync`) — compile-first, fail-closed
  publish carrying stable `rule.skin.*` rejection codes; every publish is a control change.

The bridge OWNS NO evaluator semantics: hit-policy, no-match, reified bounds, and
undeclared-ref enforcement live in `Harborline.Foundation.RuleEngine`, the single source of
truth for rejection and evaluation.

This package is an internal-only projection; public distribution is not authorized
(`HarborlinePublicDistributionAuthorized=false`).
