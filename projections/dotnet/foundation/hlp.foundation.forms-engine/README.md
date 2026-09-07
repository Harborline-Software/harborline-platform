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

This is an implementation checkpoint, not HLF-055 completion. All 80 captured source cases and all
38 migration-gap cases pass. The package and its destination-neutral authoring-host boundary are
package/consumer green. HLF-055 remains open for the authenticated inspection-review vertical slice.
