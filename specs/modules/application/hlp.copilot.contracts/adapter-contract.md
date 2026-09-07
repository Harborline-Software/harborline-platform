# Pilot host/effect adapter contract

The named seam is `PilotEffectAdapter` (TypeScript declaration in `projections/typescript/application/hlp.copilot.contracts/src/types.ts`). The application implements it. Wave 1 defines the seam but supplies no implementation.

## Crossings

| Crossing | Caller → callee | Payload | Required behavior |
|---|---|---|---|
| Read live target | Platform → app `currentContextKey(surface)` | surface id | Return an opaque key for the target visible *now*, or `null`. No model-readable data is returned. |
| Execute validated proposal | Platform → app `execute(receipt, gesture?)` | canonical surface/command, schema-validated deeply frozen args, required classify-time `expectedContextKey` | First call `assertDispatched(receipt, currentContextKey(receipt.surface), gesture)`. Compare expected/live keys immediately before acting; mismatch returns `stale-target`. NEVER has no receipt. |
| Confirm CP | App's distinct human Apply handler → `confirmDispatch(receipt, liveKey)` | minted receipt and key observed at click time | Return an identity-checked gesture proof bound to that receipt and target. `assertDispatched` consumes it once; a missing, forged, replayed or differently bound proof is refused. |
| Effect outcome | App → Platform | `{ok:true, undoToken?}` or `{ok:false, code}` | Stable localizable failure codes; no exception or partial success masquerading as success. An undo token is opaque and app-owned. |

The eventual composition layer will also need host adapters for transport, secure history, downloads, route/context snapshots, settings, boot health, and capability-specific effects. Those belong to Waves 2–5 and are deliberately not invented in this Wave-1 draft.

## Direction and trust

Provider/model output crosses only into Platform parsing as `unknown`. It never crosses directly into an adapter. Platform passes only a canonical command after schema validation and AP/CP/NEVER classification. Parsing copies and deeply freezes schema-approved data before classification so caller-held references cannot alter the receipt's args. Accessors and mutable non-data objects are refused. The application remains the sole owner of live state, authorization, policy enforcement, and mutation.

The required `expectedContextKey` is **hygiene, NOT the boundary**: a breaking change to a Wave-1 seam with no implementations. Slice 1 already required it in `DispatchReceipt`; slice 2 also refuses omitted/non-string JavaScript keys and requires a live key at the dispatch assertion. This field alone contains nothing.

Receipt and gesture identity checks contain cooperating Platform call paths. They do not contain the app author, who holds the adapter and can call `confirmDispatch` without a human or omit `assertDispatched`. No TypeScript brand or Platform-owned dispatcher is claimed as the effect boundary. The API process boundary remains slice 3's proof obligation. The host must recheck authorization under the user's identity and keep the target stable through the effect; the synchronous Platform check cannot make an asynchronous host operation atomic.

## Forbidden to cross

- Executors, stores, service clients, mutation verbs, credentials, tokens, host globals, `fetch`, React/Blazor objects, or storage handles may not enter provider-reachable Platform descriptors.
- Raw model JSON, unknown command ids, unvalidated args, NEVER commands, or a target chosen by the model may not enter `PilotEffectAdapter`.
- PII, secrets, full state graphs, or mutable object references may not enter serialized model context. Context is bounded, redacted, and app-issued.
- A CP card click cannot reuse an unchecked target. The classify-time key must be rechecked at click time and immediately before execution.
- The adapter cannot weaken a rejection, substitute another target, silently coerce args, or fall back to plaintext/unsafe execution.

## Host implementation obligations — executable pins and remaining destination proofs

The six obligations have the following executable pins. The projection test path below is `tests/obligations.test.mjs` within the Pilot contracts package; the native gate discovers it through that package's `tests/*.test.mjs`. The tooling pin runs through `node tooling/run-tooling-selftests.mjs`.

| Obligation | Pinned test | Proof scope |
|---|---|---|
| Stale-key negative test | `obligation: stale key and missing live-target key fail closed` | Production receipt/live-key guard; omitted keys also fail in two compile-negative consumers. |
| Adapter cannot be imported from provider-reachable modules | `tooling/tests/pilot-provider-imports.test.mjs`: `obligation: provider-reachable Platform modules do not import the effect adapter` | TypeScript-symbol inventory of this package's modules, excluding the public app-facing barrel. An aliased import is the positive control. App-owned modules remain unpinned because they are not compiled here. |
| CP cannot execute without a human gesture | `obligation: CP needs a distinct one-use gesture bound to receipt and target` | Production proof identity, receipt binding, target recheck and replay refusal. The destination Apply-handler wiring remains unpinned; Platform trusts the app to call the mint only after a human gesture. |
| NEVER cannot be dispatched even through a forged request | `obligation: NEVER cannot dispatch through a forged request` | Production classification and receipt membership guard, with a deliberately forged NEVER command at both AP and NEVER tiers. |
| Authorization failure is fail-closed | `obligation: authorization denial is fail-closed in the host contract` | Executable contract specimen in `tests/fixtures/contract-host.mjs`, with zero effect attempts on denial. Real host authorization remains unpinned: Wave 1 supplies no adapter. |
| Effects are atomic or report failure honestly | `obligation: effects commit atomically or report failure honestly in the host contract` | Executable contract specimen stages effects and preserves state on failure. Real host transactions and failure mapping remain unpinned for the same reason. |

`receipt args: nested mutation after classification cannot reach execute` separately pins the slice 1 deep-freeze follow-on, including nested arrays and mutation through both the caller's args and the parsed proposal.

These pins do not assert destination closure. Slice 3 owes the API policy/provenance boundary and real authorization/effect proofs; the destination app owes actual Apply-handler and adapter conformance. Slice 4 owes the app-build import/dependency fence over pack transport, pack install/catalog, HTTP, storage and service location. Slice 4 waits on ticket 107's executor placement; the Platform import pin is not a substitute. The boundary architecture tests still owe acceptance rule 7's at-least-five red-team fixtures with a pre-stated failure count.
