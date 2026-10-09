# Harborline.Foundation.ExecutionRuntime

Revision 2 of `hlp.foundation.execution-runtime`, the execution runtime substrate (DES-0056, T-528
slice 1): run-kind registration, one kind-qualified run identity, a closed run status vocabulary with
legal transitions, the substrate's named retry profiles, the dead-letter path, and a tenant-scoped
`IRunStore` port with an in-memory reference implementation. Slice 2 adds one tenant-scoped effect
receipt per logical effect, its closed effect-status vocabulary, and ambiguity reconciliation evidence.

Each engine registers its run kind and keeps its own state in its own store; the substrate holds
identity, status, attempts, retry profile, dead-letter and `caused_by` only. Effect receipts retain
their required non-secret references and evidence but never engine payloads or secret values. Engine
adoption, the execution trace and the observe channel are later T-528 slices. The package is local
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
