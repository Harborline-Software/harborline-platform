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
import { compile, FormRuleGraph, RuleInstance, RuleRowSnapshot, RuleValueSnapshot } from '@harborline-software/rule-engine'

const compiled = compile(formDefinition.rules)       // throws CompileError on a bad / cyclic def
const businessClock = () => new Date('2026-06-30T00:00:00.000Z') // supplied by the calling host
const graph = new FormRuleGraph(compiled, businessClock)
let result = graph.evaluateInstance(RuleInstance.fromJsonText(JSON.stringify(instanceBody)))

// reactive as-you-type: only the transitive dependents re-evaluate
result = graph.reevaluate('amount', RuleValueSnapshot.fromJsonText('1200'))
result = graph.addRow('lineItems', RuleRowSnapshot.fromJsonText('{"id":"r4","fields":{"qty":2,"price":5}}'))

if (result.isSaveBlocked) { /* a validity failure, errored value, or pending value */ }
```

`Pending` is this tier's async-dependency state (a `reference.*` lookup over the Bridge); a `Pending`
reaching the synchronous .NET tier at save is a fail-closed validation error (`rule.pending_at_save`).

## Pure evaluator input boundary

Evaluator entry accepts only runtime-owned snapshots. Hosts explicitly capture JSON text with
`RuleContextSnapshot.fromJsonText`, `RuleInstance.fromJsonText`, `RuleValueSnapshot.fromJsonText`,
or `RuleRowSnapshot.fromJsonText`; arbitrary objects, accessors, proxies, and forged instances are
refused without reflection. Each capture admits at most 262,144 UTF-8 bytes, 64 JSON nesting levels,
and 5,000 JSON values. Graphs take a graph-local copy of an admitted instance. The per-aggregate row
cap applies only to a table referenced by an aggregate; a refused aggregate row is never retained.
A `CompiledGraph` is likewise a compiler-owned handle: its public TypeScript
shape is inspectable, but only a result returned by `compile` is admitted to `FormRuleGraph`. These
are implementation bounds for inert evaluation data, distinct from the published authored-rule
limits.

## Build / test

Standalone (not a pnpm-workspace member; mirrors the `@harborline-software/contracts` pattern — own committed
`pnpm-lock.yaml`, gitignored `dist/`):

```sh
pnpm install --ignore-workspace --frozen-lockfile
pnpm test          # conformance corpus + reactive/bounds/guard unit suites
pnpm typecheck
pnpm lint
```

## Pinned Taxonomy coding answers

A host can opt into hierarchy-aware `coding.is` with an immutable finite capture.
Taxonomy produces the answers outside evaluation. The host independently supplies its
admitted tenant, definition version and SHA-256 binding manifest.

```ts
const coding = CodingSubsumptionSnapshot.fromJsonText(answersJsonText, expectedBindingsJsonText)
const guard = new GuardEvaluator(businessClock, limits, coding)
const graph = new FormRuleGraph(compiled, businessClock, admission, limits, coding)
```

Both documents use the versioned tuple formats in the module interface and separately
fit the existing 262,144-byte, depth-64, 5,000-value JSON envelope. Tags, types, tuple
order, unique aliases/answer keys, tenant and every exact version/hash pin are checked.
Codes and aliases remain opaque and case-sensitive. The reader never normalizes pins.
A supplied null or forged capture refuses construction. Omitting the final constructor
argument preserves legacy flat equality, coercions and array short circuit.

With a capture, the coding code is the descendant and the third operator argument is
the ancestor. Missing systems refuse with `rule.coding_binding_missing`; missing
finite pairs refuse with `rule.coding_answer_missing`. Every well-formed same-system
array member is checked, so a true member cannot hide a missing answer. Malformed
members are ignored. A completed negative answer remains false.

One graph or guard retains one capture for full, row and reactive evaluation. To use
another tenant, pin or finite query set, construct a new evaluator. Rules never fetches
definitions, calls a provider or traverses taxonomy edges. Capture hashes bind bytes;
they do not authenticate an untrusted producer. Existing borrower admission still
precedes value access. Opt-in work proofs conservatively bound each `coding.is` by
`5000 * 5000 * (sum(child maximum result bytes) + 262144 + 1)` and runtime scans charge
the existing step budget. Legacy proofs stay unchanged.

This source slice does not establish caller integration, released-package evidence or
the wider T-493 acceptance. Its authored tests require a separately admitted execution.
