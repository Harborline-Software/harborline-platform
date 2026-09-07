# @harborline-software/rule-engine (SPINE-1)

The **reactive client tier** of the SPINE-1 Tier-2 rule engine (ADR 0140 D2). The TS port of the
.NET `Harborline.Foundation.RuleEngine`: a dependency-graph evaluator over the closed
`harborline-jsonlogic/v1` operator set, with fail-closed resource bounds, authoring-time cycle
detection, reactive re-evaluation (transitive dependents only), incremental child-table edits, and
the async `Pending` state.

It drives the as-you-type form / child-table renderer; the .NET tier re-validates on save (the
integrity tier is authoritative — the client result is advisory). **Both tiers emit byte-identical
`RuleOutcome` values**, proven by the shared conformance corpus at
`../foundation-rule-engine/conformance/corpus/*.json` (loaded by both this package's vitest suite and
the .NET test project; the cross-tier diff in `scripts/rule-engine-conformance-diff.mjs` is RED on any
divergence).

**The normative operator table, scope-addressing grammar, resource bounds, and the
own-interpreter rationale (Decision DI) live in the .NET package README**
(`../foundation-rule-engine/README.md`) — the single normative source both tiers cite. This package
is a faithful port of that spec; do not let the two diverge (the corpus catches it).

## Usage

```ts
import { compile, FormRuleGraph, RuleInstance, GuardEvaluator } from '@harborline-software/rule-engine'

const compiled = compile(formDefinition.rules)       // throws CompileError on a bad / cyclic def
const graph = new FormRuleGraph(compiled)
let result = graph.evaluateInstance(RuleInstance.fromJson(instanceBody))

// reactive as-you-type: only the transitive dependents re-evaluate
result = graph.reevaluate('amount', 1200)
result = graph.addRow('lineItems', { id: 'r4', fields: { qty: 2, price: 5 } })

if (result.isSaveBlocked) { /* a validity failure, errored value, or pending value */ }
```

`Pending` is this tier's async-dependency state (a `reference.*` lookup over the Bridge); a `Pending`
reaching the synchronous .NET tier at save is a fail-closed validation error (`rule.pending_at_save`).

## Build / test

Standalone (not a pnpm-workspace member; mirrors the `@harborline-software/contracts` pattern — own committed
`pnpm-lock.yaml`, gitignored `dist/`):

```sh
pnpm install --ignore-workspace --frozen-lockfile
pnpm test          # conformance corpus + reactive/bounds/guard unit suites
pnpm typecheck
pnpm lint
```
