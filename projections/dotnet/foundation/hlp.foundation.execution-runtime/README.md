# Harborline.Foundation.ExecutionRuntime

Revision 4 of `hlp.foundation.execution-runtime`, the execution runtime substrate (DES-0056, T-528
slice 1): run-kind registration, one kind-qualified run identity, a closed run status vocabulary with
legal transitions, the substrate's named retry profiles, the dead-letter path, and a tenant-scoped
`IRunStore` port with an in-memory reference implementation. Slice 2 adds one tenant-scoped effect
receipt per logical effect, its closed effect-status vocabulary, and ambiguity reconciliation evidence.

Each engine registers its run kind and keeps its own state in its own store; the substrate holds
identity, status, attempts, retry profile, dead-letter, `caused_by` and waits (step id, reason, registration
time, timer wake-up and claim) only. Effect receipts retain
their required non-secret references and evidence but never engine payloads or secret values. Engine
adoption, durable storage and the observe channel are later T-528 slices. The package is local
and distribution-blocked.

T-1028 adds the owner-approved compensation association. The original receipt's
`CompensationEffectId` names its independently recorded compensation receipt; that receipt's optional
`CompensatesEffectId` names the one original effect it compensates. The workflow engine stamps the
backlink from the declared compensating-step execution context. A compensation may also carry its
own forward `CompensationEffectId`; the two associations have distinct meanings.

`EffectReceiptLedger.CheckCompensationAsync(tenant, originalEffectId)` is a separate read-only check.
For `Compensated` and `CompensationFailed` originals it requires a target in the same tenant whose
identity and backlink match. Missing or inconsistent receipt evidence refuses
`execution.effect_receipt_invalid` and identifies the known effects; malformed identities retain
`execution.effect_identity_invalid`. A missing original is refused by its own id. Other original
statuses impose no compensation check. It does not traverse compensation chains, compare workflow
runs or target statuses, or prove the external compensation's business correctness.

Recording remains shape-only, so the original can be recorded before the compensation. Ordinary
and legacy receipts without the optional backlink remain recordable and readable, but a legacy
target cannot pass association verification without authoritative evidence. No historical link is
inferred or backfilled. The store port is unchanged.

The workflow producer and post-record caller are a later T-488 integration dependency: after both
receipts are durable, the engine must check their association and recover a crash between recording
and checking. Recovery or audit may repeat the check. This substrate slice does not claim that
engine integration, recovery adoption or authorized observe wiring is implemented.

## Due retries and timer waits (revision 3)

`RunKindRegistry.Register(kind, engine, handler)` optionally registers an async handler receiving
`(claimedRun, timerStepId, cancellationToken)`; a null step id means the next retry attempt already began.
Existing two-argument registrations remain valid. `DueRunDispatcher.DispatchAsync(tenant)` performs one
pass using the injected `TimeProvider`, returning due run ids whose kinds have no handler without claiming
them. A host calls it for each tenant; engines add no timer loops. Handler exceptions propagate to the host.

The dispatcher requires the stable node id, unique among live nodes. Before the first pass for each tenant
it clears only that id's timer claims. Timer leases default to five minutes and can be set in the constructor.
An unexpired claim excludes every dispatcher; expiry makes it eligible again. No renewal is implemented.
Retry claims reuse `RunLifecycle.BeginAttemptAsync`; a failed holder leaves a running attempt for ck-11's
future timeout implementation, with no retry lease.

A running record carries `Waits`, immutable `RunWait` values with step id, closed reason, UTC registration
time and (only for a timer) a fixed absolute UTC wake-up time. `Hold()` and `Release()` preserve that time.
The store copies wait collections at every read/write boundary. Engines persist wait changes with the run's
expected version. A timer handler must resolve its delivered step using the **delivered** run version as its
fence, never reload a newer version and overwrite it. After another claimant advances the version the old
write is refused. A handler must check for an already-resolved step and make repeat deliveries a no-op.
Resolution, the human deadline race, durable storage, and engine adoption belong to their own slices.

Contract source: control `2c983d1e3`, ADR-0118, ADR-0132, ADR-0143 and ADR-0147. Acceptance tests:
`DueRunDispatcherTests.cs` and `RunWaitsTests.cs` in the adjacent test project. Each test records its oracle
and named mutation target; measured mutation evidence is reported separately and is never inferred from
those comments.

## Execution receipt and trace (revision 4)

`ExecutionReceipt` is a run's execution receipt (DES-0056 `execution-runtime-ck-3`): tenant, kind-qualified
`RunId`, `RunStatus` and the applicable Access decision's opaque stable identity. The receipt links that
decision and never carries or recomputes Access's evidence (`ck-2`); with no decision it is null and stays
null. `ExecutionTraceStep` is one step of the run's own progress (`ck-4`): a 1-based `Ordinal`, a `Phase` and a
verbatim `Status`, distinct from Access's four-stage trace. Both validate on construction and expose get-only
properties. The field names match Layout's `LayoutExecutionReceipt` consumer shape.

Engines emit both through `IExecutionReceiptStore` (`eng-1`, `cc-1`). `PutReceiptAsync` stores or replaces the
tenant's one current receipt for a run. A replace may not add, remove or change the Access decision link
(`execution.access_decision_changed`), and its status must stay the same or move along the run transition table
(`execution.transition_illegal`). `AppendTraceStepAsync` accepts only the next ordinal. An exact repeat of a
stored step is a no-op, so a replayed append after a crash does not fail; a different step at a taken ordinal is
refused. The reads return the receipt (or null) and the steps in order (or empty). Every key is tenant plus kind-qualified run identity.
`InMemoryExecutionReceiptStore` is the reference implementation. The durable adapter and restart proof
(`T-528/durable-run-store`), the authorized observe read (`T-528/observe-read`) and engine emitting are later
slices. Acceptance tests: `ExecutionReceiptTests.cs`.
