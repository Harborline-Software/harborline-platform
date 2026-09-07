# Pilot host/effect adapter contract

The named seam is `PilotEffectAdapter` (TypeScript declaration in `projections/typescript/application/hlp.copilot.contracts/src/types.ts`). Platform calls it; the application implements it. Wave 1 defines the seam but supplies no implementation.

## Crossings

| Crossing | Caller → callee | Payload | Required behavior |
|---|---|---|---|
| Read live target | Platform dispatcher → app `currentContextKey(surface)` | surface id | Return an opaque key for the target visible *now*, or `null`. No model-readable data is returned. |
| Execute validated proposal | Platform dispatcher → app `execute(request)` | canonical surface/command, schema-validated args, classify-time `expectedContextKey` | App compares expected/live keys immediately before acting; mismatch returns `stale-target`. CP requests are callable only after a distinct human Apply gesture. NEVER is never callable. |
| Effect outcome | App → Platform | `{ok:true, undoToken?}` or `{ok:false, code}` | Stable localizable failure codes; no exception or partial success masquerading as success. An undo token is opaque and app-owned. |

The eventual composition layer will also need host adapters for transport, secure history, downloads, route/context snapshots, settings, boot health, and capability-specific effects. Those belong to Waves 2–5 and are deliberately not invented in this Wave‑1 draft.

## Direction and trust

Provider/model output crosses only into Platform parsing as `unknown`. It never crosses directly into an adapter. Platform passes only a canonical command after schema validation and AP/CP/NEVER classification. The application remains the sole owner of live state, authorization, policy enforcement, and mutation.

## Forbidden to cross

- Executors, stores, service clients, mutation verbs, credentials, tokens, host globals, `fetch`, React/Blazor objects, or storage handles may not enter provider-reachable Platform descriptors.
- Raw model JSON, unknown command ids, unvalidated args, NEVER commands, or a target chosen by the model may not enter `PilotEffectAdapter`.
- PII, secrets, full state graphs, or mutable object references may not enter serialized model context. Context is bounded, redacted, and app-issued.
- A CP card click cannot reuse an unchecked target. The classify-time key must be rechecked at click time.
- The adapter cannot weaken a rejection, substitute another target, silently coerce args, or fall back to plaintext/unsafe execution.

## Host implementation obligations (AUTHORED-ONLY)

These are destination proofs, not pinned closure rows: stale-key negative test; adapter cannot be imported from provider-reachable modules; CP cannot execute without a human gesture; NEVER cannot be dispatched even through a forged request; authorization failure is fail-closed; effects are atomic or report failure honestly.

