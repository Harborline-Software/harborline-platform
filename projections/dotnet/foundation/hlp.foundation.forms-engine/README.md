# Harborline Forms Engine

Local-shadow implementation of the complete tenant-scoped Dynamic Forms execution module. The
package is intentionally distribution-blocked while HLF-055 is in progress.

Revision 1 owns render, validation, deterministic calculations, field security, atomic submission/
audit/outbox persistence, idempotency, and projection recovery behind one caller interface. It does
not reference Harborline App, API, any external consumer product, React, Blazor, earlier source, or the migration control
plane.

The current preview implements the caller interface and production-owned seams, including one
current request scope per operation, published-definition and reuse resolution, role-aware
validation, schema and deterministic rule evaluation, calculated/pruned accepted values, opaque
protection before persistence, atomic submission/audit/outbox commit, canonical idempotency,
immediate projection delivery, and restart recovery. Production composition requires every context,
state, schema, security, audit, transaction, and projection provider. The bundled in-memory
submission adapter explicitly refuses Production.

Submission preparation now runs inside the actual submission-store exclusion scope. The engine
captures one injected-clock instant for a render or submit pipeline, materializes and evaluates the
candidate under that scope, then gives the accepted candidate's fingerprint, record, and audit to
the kernel transaction boundary for its normal identity and staging checks. A refusal, cancellation,
or preparation error rolls the scope back without a submission, audit, outbox frame, or projection
delivery. Both built-in stores expose a one-commit scope; the file journal appends only after the
complete accepted envelope has been prepared, and projection delivery starts only after scope
disposal.

Revision 5 adds the production current-request context seam. It preserves the pinned Forms macaroon
wire and HMAC chain while requiring an action-bound Harborline identifier, strict caveat cardinality,
and one opaque denial. This rejects legacy actionless tokens that a holder could otherwise attenuate
by appending the first authority-granting action. The adapter binds tenant/subject/action
to the current actor, resolves Party once, and always takes roles from the current host-authenticated
actor. Bearer role caveats remain parseable for source compatibility but never enter authorization.
Each token carries exactly one action, preventing a bearer holder from widening authority by
appending another additive action caveat to the source-compatible HMAC chain.
The current-request adapter intentionally denies background recovery because the pinned source has
no recovery bearer action; production workers must register a separate job-scoped context adapter.

Revision 6 closes the process-restart persistence boundary. The built-in file submission adapter
holds an exclusive process lock, appends versioned SHA-256-checksummed frames, flushes each commit,
retry, or completion before mutating in-memory indexes, truncates only an incomplete final frame,
and refuses complete-frame corruption or unknown versions. Authenticated headers prevent a corrupt
length from masquerading as a partial tail; in-process leases suppress concurrent delivery while
projection sinks remain responsible for idempotency across crash retries. Abrupt separate-process
tests prove committed submission/audit/outbox replay and pending projection recovery. This adapter is intentionally
single-process and single-node; deployments requiring distributed leases or multi-node high
availability must supply another `IFormSubmissionTransactionStore` implementation.

Current caller-interface parity evidence includes the shared validation corpus, exact-decimal table
aggregation, indistinguishable missing/cross-tenant reads, owning-tenant ordinary-value rendering
with sensitive withholding, candidate-byte diagnostics, and structured constrained-schema errors.
Revision 8 closes the authoring-host and package boundary. Authoring hosts register `AddHarborlineFormsAuthoringPublisher` and save through
`IFormDefinitionAuthoringPublisher`. The bounded service calls `RuleCompileAdmission.ValidateOrThrow`
and policy admission before its first definition-store mutation, then atomically registers and
publishes the admitted Draft. It
rejects malformed or cyclic Tier-2 rules, unsupported Power Fx, and uncompilable page guards with
stable Forms definition codes while leaving Tier-1 JSON Schema rules to the schema registry. It also
rejects unresolved or relaxing classification/access/lifecycle policy, unsatisfiable residency, and
unacknowledged sensitive connector inputs. A disposable minimal HTTP authoring host references only
`Harborline.Foundation.Forms.Engine`, exercises valid and rejected requests over loopback, resolves
the seven-artifact transitive Harborline closure, and proves zero source/project fallback or assembly
ambiguity.

Revision 16 adds durably accepted Records commands (plan W6/W7, ADR-0110 accepted 2026-10-12; its readings
are isolated in `FormAcceptedCommandReadings`). A submit whose published head targets a `form_only` Record Type is
accepted, not delivered: its submit gate is decided inside the acceptance transaction, and the one existing atomic
commit carries the submission, the audit with the gate's own evidence as its authority snapshot, and the delivery row
with a `FormAcceptedRecordCommand` (command id, a server-derived operation id, the target, the payload fingerprint, the
acceptance instant and snapshot). The submission store must implement `IFormAcceptedCommandStore`; both built-in stores
do, and the journal writes such frames as format 3. The command executes only as the configured
`FormEngineOptions.AcceptedCommandWorkerId`, never as the submitter, through `IFormProjectionSink.DeliverAcceptedAsync`,
after every stored binding is validated again and after a durable claim; recovery executes an interrupted claim again,
idempotently on the original operation. `FormAcceptedCommandCancellation` cancels a pending command under the dedicated
`records.command.cancel` permission; cancel and execution contend on the store's claim, so exactly one wins, and a cancel
after the claim is `records.command.cancel_too_late`. Ordinary submissions, and forms targeting a `role` type, keep the
existing delivery path. A `role` target's failed inline delivery is retried only through the port's single-row lease in
its submitter's replay (revision 17 states what a store without the port does). Under the decision 1 reading the host's
submit-gate decider runs while the store scope is held, so it must not open its own kernel transaction (the submit is
then denied) and should be fast. Host enablement stays held.

Revision 16 also adds: an audit envelope with every cancellation and refusal, in the same atomic
transition; queue-ordered leasing (least recently enqueued first) and a bounded retry before a claimed command with a
broken binding is quarantined; `IFormProjectionSink.ExecutesAcceptedCommands`, without which a command is never claimed;
no recovery of a targeted row without a command (never acting as the submitter); the acceptance snapshot bound to the
command's own decision (`FormAcceptedCommandEvidence`); the mapped Records body digest bound at acceptance
(`IFormAcceptedRecordBodies`); and
`form.engine.store-reentrant` plus `AcceptanceDecisionTimeout`, so a decider inside the acceptance can neither deadlock
the store nor hold its scope indefinitely.

Revision 16 also never makes a claimed command terminal while an earlier run may have committed its
effect (DES-0029 section 9): such a command is `Quarantined` instead, audited, non-terminal, visible in the pending pool
and never leased again, after `AcceptedCommandBindingRetryLimit` binding failures (only binding failures count; a form
revision that is not found is an infrastructure failure) or a Records refusal after a resumed claim. A cancel abandons a
quarantined command only when the host's `IFormAcceptedCommandEffects` lookup establishes that its effect did not and
cannot commit, and leaves quarantine as completed only through `FormAcceptedCommandResolution`, audited, when the same
lookup finds its effect committed (`CompleteProjectionAsync` refuses a quarantined row). Leasing rotates (least recently enqueued first; a failed row goes to the back), so no row waits forever
and stalled rows never starve fresh ones; the mapped-body digest is held to the acceptance bound;
and a `role` target's failed inline delivery is retried only by its own submitter's replay.

Revision 17 changes text only and makes the interface describe the merged behaviour. A store without
`IFormAcceptedCommandStore` refuses any targeted submit (`form.engine.provider-unavailable`) before the gate is asked,
including an idempotent replay of an existing targeted submission, as it refuses a `form_only` one. Recovery on such a
store rotates a targeted row without a command to the back of the queue with `RetryProjectionAsync` (one recorded
attempt) instead of releasing it in place, and still never delivers it. The base port's `LeasePendingAsync` defines no
order, so the rotation protects deliverable rows only on a store that leases a newly retried row behind the rows already
waiting, as both built-in stores do; a host store that ignores recorded attempts is not protected (a documented limit).
The lease-order sentence that said "by attempts, then submit instant, then outbox id" now names the queue order, and
the sentence that said a claimed command with a broken binding "is refused after" the retry limit now says it is
quarantined.

This is an implementation checkpoint, not HLF-055 completion. All 80 captured source cases and all
38 migration-gap cases pass. The package and its destination-neutral authoring-host boundary are
package/consumer green. HLF-055 remains open for the authenticated inspection-review vertical slice.
