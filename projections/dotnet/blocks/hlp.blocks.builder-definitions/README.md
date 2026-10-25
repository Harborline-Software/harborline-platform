# Harborline Blocks Builder Definitions

Local-shadow .NET package for the shared definition catalogue substrate. It provides advisory key suggestion, tenant-scoped durable archive lifecycle, and a projection-neutral platform package manifest with deterministic JSON export and validated replay into an empty catalogue.

`PlatformPackageContent.Unresolved(reference)` keeps DES-0007 policy gaps explicit. Validation refuses unresolved content rather than choosing a default. `PlatformPackageReplayer` validates the complete manifest before one atomic apply-to-empty call, and preserves declared bootstrap order in that batch. The target owns the atomic all-or-none commit and non-empty check. Stable refusal codes cover empty manifests, a missing or repeated first package record, non-empty targets, duplicate ids, unknown or reordered stages, missing or forward dependencies, and unresolved content.

`PlatformPackageSeed` is the ADR-0097 fixture for `platform-package-ck-1` through `-ck-14`. Its checked-in `_shared/packs/platform/platform-pack.export.json` is embedded in and packed with the assembly; `VerifyCheckedInExport` proves it is byte-identical to the provider-neutral closure, manifest, and SHA-256 digest document produced by the exporter. The fixture does not choose the still-open first-authority source or preloaded Access-pack provenance kind.

The ck-14 governance default `creationGate` is read only through `PlatformCreationGateCompatibility` (decision K6). The released revision 1.0.0 keeps its bytes and signatures, so it still spells the default `"form-only"`, and that exact spelling is read as `RecordCreationGate.FormOnly` for 1.0.0 only. The seed writes `SeedSpelling(revision)`: the released spelling for 1.0.0 and `form_only` for every new revision. A new revision reads exactly `form_only`, `role` or `engine`. Any other value, a value spelled for another revision, a non-string, an absent member, or a `ck-1` version that disagrees with the export's `revision` refuses `platform-package-creation-gate-malformed`; no published revision defines a default for an absent value. `PlatformPackageSeed.CreationGate` reads the checked-in export this way.

`PackageSafetyFloorReattachment` owns the released raise-only rule for a reattached package candidate. It restores a missing `safetyFloors` object or seeded member, clamps a lower integer to the seed value, and refuses `platform-package-safety-floor-malformed` with the member name when a seeded floor is present as a non-integer. It returns an independent content snapshot and never mutates either input. Each restored or raised member is reported in `Clamps`. `SafetyFloorAuthoring` is the authoring side: a read reports each sealed member's writability and reason, and authoring refuses every lowered, missing or malformed member as a code and RFC 6901 pointer without changing anything, or refuses outright without the host-supplied Access verdict for `rules:author-floor`. `RuleDefinitionCatalog` takes the host's `RulesCapabilityCheck`: authoring operations need `rules:author`, publication and release materialisation need `rules:publish`. `RuleCrossPackageAuthoring` is an authoring-only check of cross-package references against the consumer `requires` clause and the producer's exposure; publication and installation gates are T-615's. `LayoutCrossPackageAuthoring` applies the same authoring-only check to a Layout surface's edges (`layout-auth-30`), reading a `requires` entry that names the producer's package as the declared dependency. Layout admission raises every refusal as a `DefinitionRefusalException` whose stage the caller states: authoring is `Author`, publication and pack export are `Publish`, a host's page register is `Install`, and the persisted-value gate before render is `Render`.

The 13 Workshop navigation item IDs and the `catalogue:read`, `records:read`, and `audit:read` bindings preserve the migration authority in `harborline-api/_shared/packs/platform/platform-pack.export.json`. DES-0007's broader grouping remains future authoring grammar; this fixture does not silently mint replacement identities.

## Shared versioned-definition store (T-620)

`IVersionedDefinitionStore` is the registry-neutral persistence contract. Its in-memory reference
implementation promotes the Layout session's locked revision map, defensive snapshots, semantic
version comparison and restore-as-draft lifecycle. Source: `LayoutDefinitionStore.cs` in platform
commit `e2f4bb6cbe6062d9aab0b590a1708608b8f8bcc2` (`handoff/t620-layout-store-source`). The shared
implementation removes Layout types and admission calls; Layout-specific exports stay with Layout.

A `DefinitionKey` contains tenant, registry and definition id. Every `DefinitionDocument` also
contains an opaque version id, a semantic-version label and an immutable JSON body string.
`DefinitionBinding` stores both the definition key and version id. Production resolution returns
published bodies only. The head selects the highest published semantic version; it never follows
a draft. `DefinitionKind` values are catalogue namespaces, not transport content kinds or primitive
numbers. Values 0 and 1 preserve existing Forms/Workflows archive namespaces; Layout retains 3.

Host composition supplies a pure admission function for each supported registry. Missing registry
admission fails closed. Member adapters validate their own typed source, numeric constraints and
metadata consistency; they return stable codes and RFC 6901 pointers. The store validates version
syntax and JSON syntax, retains the supplied body bytes and digest, and never normalizes a body.
Metadata lives outside the body so a generic restore can copy source without understanding it.

Every mutation requires the expected tenant/registry/definition stream revision and a request id.
Exact replay returns the original result; changed operation, fence or payload refuses. Validation
runs outside the store lock, followed by a second revision check before committing. Concurrent
edits cannot overwrite each other. Published versions never change, equal-precedence versions
cannot replace an existing published identity, and restore appends a new draft. History records
each accepted lifecycle event rather than rewriting the earlier draft or publication event.

`InMemoryVersionedDefinitionStore` is a reference implementation, not a durable host adapter.
Durable adapters must atomically commit the revision, history, published head and replay result.
Rules binds first in T-588; Layout, Records, Views and Data exchange retire their member stores
through their own slices. Forms, Workflows and Aggregates retain their existing stores under T-620's
explicit scope boundary. No released host consumption is claimed here.

## Layout producer (T-580)

The Layout producer adds the platform-owned surface contract: versioned envelopes, one ordered recursive block tree for screen and page media, typed bindings, intent, portable placement, page geometry and masters, authored interactions, and immutable form references. `LayoutDefinitionAdmission` is the common structural authoring and publish validator. Its numeric limits come only from `LayoutDefinitionSchema`; the React and Blazor persisted-value entry points use the same validator and refuse invalid storage without clamping. Admitted definitions export through the existing provider-neutral package content boundary. The shared versioned store, publication concurrency, immutable history, restore and production resolution belong to T-620; this producer neither persists nor publishes. Runtime layout, binding resolution, authorization, and editors remain outside this slice.

`_shared/layout/placement.schema.json` is also embedded by Forms, so its section and item admission use the same four numeric ranges. Hosts supply one immutable `LayoutBlockKindRegistry` to producer and persisted admission. `LayoutComposition.Detach` copies a pinned surface to an independent draft candidate; Form, Template, and Report identities remain separate and no synchronisation link is created. The shared catalogue owns resolving that pin and storing the candidate.

`LayoutPackIdentity` assigns content kind **17** and primitive bucket **12** additively, after the existing transport values 0–16 and 0–11 respectively. Archive `DefinitionKind` values are a different namespace; Forms remains 0 and Workflows remains 1. This producer carries the new wire identity in its export entry. It does not modify API transport, installation, the platform seed, or its pack exporter.

## Rules catalogue (T-588)

`RuleDefinitionCatalog` supplies create, load, exact version load, stable name/id listing,
duplicate, archive, draft save, publish, restore and caller-policy resolution. Register
`DefinitionKind.Rules` with `RuleDefinitionCatalog.Admit` in the shared store. The adapter
uses foundation's strict typed source and full compiler admission; foundation never depends
on blocks. Create fences revision zero. Duplicate copies source into a new identity and
independent history. Listing scopes the exact tenant, reads shared keys and returns detached
source; `includeArchived` exposes archived rows. Archive only changes list visibility and
never revokes a published pin.

Callers supply versionId, semantic version, requestId and expectedRevision; there is no Rules
patch allocator. Latest selects the highest published generic SemVer head, so an older history
arrival cannot lower it. Pinned binds the exact admitted label to an immutable published id,
independent of later heads. Draft resolution is sandbox-only; production refuses every draft.
The legacy offline `RuleRegistry` watermark remains separate and is not this shared store.
VersionPolicy is a caller selector, absent from authored source. Restore keeps original body
bytes while reconstructing source with the new shared identity and version metadata.

## Taxonomy catalogue (T-493 S7, DES-0024)

Register `DefinitionKind.Taxonomy` (14) with `TaxonomyDefinitionStore.Admission(window)` in the
shared store, then `AddTaxonomyDefinitionStore()` registers one `TaxonomyDefinitionStore` over it.
The key is the tenant plus the dotted `TaxonomyDefinitionId`; the version id is the definition's
own three-part version. Author runs Author-phase Taxonomy admission, so an Authoritative scheme is
never saved as a tenant draft; publish runs Publish-phase admission and refuses a body whose
tenant, id or version disagrees with the catalogue (`definition.catalogue_mismatch`). Resolution
is pinned-only: `ResolveAsync` returns the exact published version or `TaxonomyDefinitionNotFound`
(absent, draft, other tenant), and a reference with no exact version refuses
`definition.reference_unpinned` at `/version` rather than resolving to a head. Foundation never
depends on blocks.

## Record Type policy declarations (T-615, partial)

`RecordTypeDocument` and `NewRecordType` add nullable init-only `AmendmentPolicy` and
`HistoryPolicy`, preserving their positional constructors. Amendment policy accepts exactly
`{"reason_required":true}` or `{"reason_required":true,"mode":"supersession"}`;
`"history_policy":"enabled"` remains the only history declaration. `RecordAmendmentPolicy.Mode`
is nullable and supports only `RecordAmendmentMode.Supersession`. Dedicated converters reject
unknown or duplicate decoded members, unsupported values and explicit null mode with the existing
`records.document_invalid` code and empty pointer. Absent or null policy members remain legacy
absence. Reason-only canonical bytes stay unchanged; explicit mode serializes as
`{"mode":"supersession","reason_required":true}`. Canonical serialization retains explicit defaults.

Only `CreateDraftAsync` initializes missing declarations to reason required and history enabled;
it does not select an amendment mode or enable temporal writes.
Save carries an omitted declaration from the accepted expected revision, so a positional resave
retains it and an exact historical retry retains its original payload. Shared catalogue replay and
CAS still refuse an unseen stale write. Save, publish, restore and install supply no policy default.
The unchanged legacy body and its digest remain valid; new creation has a different canonical body
and digest. A create retried across the upgrade can refuse `definition.replay_conflict`, and older
readers reject the new body. The held T618 grammar fixture keeps its original bytes.

`RecordTypePublication.PolicyWarnings` defaults to empty. Fresh and replayed `PublishAsync`
return `records.retention.unset` at `/retention_policy`, computed from the returned immutable body.
`ResolvePublishedSchemaAsync` binds without publication warnings; warnings are absent from refusal
and authoring diagnostics. Publication continues warning until retention becomes authorable.

The reason-only declaration implements the bounded default, carry-forward and warning slice;
it selects no mode. The additive temporal library below requires explicit supersession mode,
reason required and history enabled in the selected admitted immutable publication. History or a
reason-only policy never implies that opt-in. Retention, wider ck-20/ck-21 and host qualification
remain open; the temporal library does not change the meaning of `RecordedAt`.

## Records write: one record create (T-616, bounded)

`RecordWriteOperation.CreateAsync` is the first public Records write. It composes existing producers only:
`AccessProvider` over the host's sole `IAuthorizationDecider`, `RecordTypeDefinitionStore` and its registered
schema, `ISchemaRegistry` validation, `KernelTransactionBoundary.ExecutePreparedAsync`, and, since interface
revision 21 (records-eng-15), the two field runtimes from `Harborline.Contracts.Fields`: `IFieldKindRuntime fieldKinds`
(the compiler's admitted field-kind runtime) and `IFieldDomainRuntime fieldDomains` (the runtime over the tenant's
complete domain sources and the executor's read authority). Both are mandatory constructor inputs. The host supplies one
transaction port and one `IRecordWriteReceiptReader` over the same committed state. Hosts do not enable the operation
yet; the API adapter is T-619.

On both create paths (`CreateAsync` and `CompleteAcceptedAsync`), after schema validation and before anything is
staged, each bound field's kind binds (`Bind`), its declared constraints intersect over the tenant's domain sources in
the executor's scope (`IntersectAsync`), and its submitted value validates (`Validate`). The first failing field refuses
with the existing `validate` / `entity.validation.body_invalid` at that field's escaped RFC 6901 pointer, and nothing is
staged; no field-runtime diagnostic is disclosed.

The gate decides every call first, a retry included, so a denial discloses no receipt and no conflict. A committed
operation is looked up by ambient tenant, authenticated caller and stable `operation_id`
(`RecordWriteOperationKey`). Another caller or tenant is another scope and never sees that receipt. A committed
operation is never re-executed. The same intent returns the original receipt with its original version, definition
digest and schema, even after a newer publication. A changed body, Record Type or explicit pin refuses
`records.write.replay_conflict` with the pointer `/operation_id`. A first-seen operation binds its explicit pin, or the
published head when unpinned, so a newer publication governs the next new write.

The operation id is the request's `operation_id` member, taken exactly as sent: nonempty, at most 255 UTF-8 bytes,
compared ordinally, and never trimmed or normalized. So `" op"`, `"op "` and `"op"` are three operations, and so are
the NFC and NFD spellings of one name. An empty, longer or non-text id refuses `records.write.operation_id_invalid` at
`/operation_id` in the validate stage, after the gate and before any lookup. The rule is approved (D6); the spelling of
that code is proposed.

A lookup miss (the reader returns null) is the only way to a first-seen write. An operation that is already accepted
is settled from what was stored, by one rule, wherever it is found:

- the lookup returns it, or
- the host's commit settles the staged operation identity as `Replayed` or `Conflict` (a concurrent first write) and
  returns the stored operation as `RecordWriteCommit.Stored`.

The rule:

- a stored encoding version this producer does not write, or a blank stored digest, refuses
  `records.write.receipt_unsupported`;
- another intent refuses `records.write.replay_conflict`, whether or not a receipt is readable;
- the same intent needs the stored receipt for this operation id, or refuses `records.write.receipt_unsupported`.

A commit that reports `Replayed` or `Conflict` without `Stored` refuses `records.write.receipt_unsupported`. Nothing
is disclosed on any of these refusals. A host reader returns an accepted operation even when its receipt is unreadable,
never null. A `Created` commit must return the new receipt naming the requested operation id. Otherwise it refuses
`records.write.receipt_unsupported`; the record and its authority snapshot were committed by the host, so no `Decision`
is returned, and a retry settles from the stored operation.

Insert-freedom differs by path:

- On the lookup path it is structural: nothing is staged.
- On the commit path it rests on the host. **Host conformance (T-619 qualification):**
  - the store keys committed operations by `RecordWriteOperationKey.IdempotencyKey` under a uniqueness constraint
    enforced inside the commit;
  - a `Replayed` or `Conflict` commit publishes no record, audit or authority snapshot;
  - it returns the operation exactly as stored.

  Test this against the real store, including two concurrent first writes.

Retry identity is the intent, `records-write-intent/1` (proposed; byte grammar on `RecordWriteIntent`). It is
structural and exact: `3`, `3.0` and `3e0` are one number and the string `"3"` is another; object member order is
ignored at every depth; array order and `null` versus a missing member are meaningful. Numbers are never converted to
binary64, and an exponent is never expanded or parsed at arbitrary precision: its leading zeros are skipped, and a
written exponent with more than 18 significant digits (magnitude 10^18 or more, whatever the significand) has no intent,
so encoding costs time and memory linear in the body. Every number within that bound encodes exactly. That
normalization is for comparison only. The validator and the store
receive the submitted body with object members in ordinal key order, every number in its submitted text, and every other
value equal to the submitted one (the JSON writer may re-escape strings). So `3.0` under `fraction_digits` 0 still
refuses `field.fraction_digits_exceeded`, and the committed body keeps `3.0`. A body string that is not Unicode text
(a lone surrogate), or a body number whose exponent is beyond that bound, has no intent and refuses
`entity.validation.body_invalid` with no pointer before any lookup (a provisional mapping). The encoding version is
persisted with the committed operation.

Refusals carry an ADR 0038 stage, a stable code and RFC 6901 pointers. They never carry the body, an intent digest or
authority evidence. The codes are:

| Code | Stage | Pointer |
|---|---|---|
| `entity.validation.schema_unknown` | bind or validate | none |
| `entity.validation.body_invalid` | validate | the body's pointers |
| `records.write.operation_id_invalid` | validate | `/operation_id` |
| `records.write.replay_conflict` | commit | `/operation_id` |
| `records.write.receipt_unsupported` | commit | none |
| `records.write.form_only` | bind | `/record_type_id` |
| `records.write.engine_only` | bind | `/record_type_id` |

Under D6 (approved 2026-10-10), `records.write.replay_conflict` and `records.write.receipt_unsupported` are the only
codes for their conditions. A host adapter maps them as they are and emits no competing code for the same condition,
for example `authorization.idempotency_key_reused`, a forms idempotency code or `definition.replay_conflict`. So a
Records write route does not also pass through a generic idempotency-key layer.

The audit payload of a create has exactly seven identifier keys, in this order: `act` (`records:write`),
`operation_id`, `record_id`, `record_type_id`, `version`, `definition_digest`, `schema_id`. `version` is the published
Record Type definition version the write bound to, the same version the receipt reports. It is not the intent
encoding version, a schema version or a payload format version. The payload has no body value, no intent digest and no
authority evidence. Format versioning, tenant, the actual executor, submitter provenance and outcome belong to the
audit envelope, never the payload. `KernelAuditEvidence` carries the audit id, the executor (`ActorId`), the recorded
instant and the authority snapshot. It has no format-version, tenant, on-behalf-of or outcome member yet; that is an
open envelope gap, not a payload key.

The gate is asked once, through `AccessProvider.CheckWithEvidenceAsync`. A create stages the gate's own
`AuthorizationDecisionEvidence` as its audit's `KernelAuditEvidence.AuthoritySnapshot` inside the one prepared
command. The snapshot therefore commits or rolls back with the record, its operation identity and its audit in one
transaction through `KernelTransactionBoundary` (ADR 0068). There is no append after the commit, and the create's
outcome returns no `Decision`. The snapshot bytes are the evidence's tenant, principal, trace version and four projected
steps (the `AuthorizationTraceSnapshot` shape; byte grammar proposed for review).

The payload and the snapshot are compact UTF-8 JSON written by the default `Utf8JsonWriter`. Its encoder escapes `+`
as `\u002B` (for example in the snapshot's `at:` fact), and escapes non-ASCII and HTML-sensitive characters as
`\uXXXX` (for example in a non-ASCII `operation_id`). The bytes are deterministic, but consumers parse the JSON and
never compare it byte for byte.

Every other outcome (a denial, a refusal after the gate, a replay or a conflict) commits nothing and returns `Decision`
for the host to persist through its existing trace plumbing, so a denial keeps its four-step trace (RW-1). `Decision`
is null only when the request never reached the gate or the gate's evidence named another context.

There is one exception: `records.write.receipt_unsupported` after the host's `Created` commit returned a missing or
foreign receipt. It is the only refusal after which something persisted: the record, its identity, its audit and the
snapshot all committed. So `Decision` is null, `Committed` is false (no usable receipt came back), and a retry with the
same `operation_id` settles from the stored operation. The trace's target
is the record id minted for that attempt; for a replay or a refusal no such record exists, so a host joins a trace to
its operation by tenant, caller, `operation_id` and instant, never to a receipt by record id. A fault after an allowed
gate propagates without `Decision` and commits nothing. Hosts do not log the authority snapshot, the operation's
fingerprint or a stored intent digest, and the operation itself emits no telemetry.

### Conflict policy and categories (T-615, records-ck-16 and records-ck-23)

Two optional declarations, both added as nullable init-only members so positional constructors and legacy bodies keep
working:

- A field's `conflict_policy` says how an offline conflict on it is settled: `ask` (the default), `additive` or
  `latest_observation`, spelled exactly so (L1011). There is no last-write-wins spelling. Any other spelling, a different
  case or a number is `records.document_invalid`. The canonical body omits an explicit `ask`.
- A Record Type's `categories` are text tags, many per type and independent of the automatic package label (L103), kept
  in authored order. A null element is `records.document_invalid` at `/categories/{i}`; a blank tag refuses
  `records.categories.blank` and a repeated tag (ordinal) `records.categories.duplicate` at `/categories/{i}`, in every
  phase. The canonical body omits an empty list.

Every existing body, digest and compiled record schema is byte-identical. Both members are declarations only: raising
and settling an offline conflict belongs to the offline runtime (records-eng-25), and nothing yet reads the categories.

### Creation policy (decision D1, K9; plan W2 and W9)

A Record Type declares who may create its records (records-ck-22):

- `creation_gate`: `form_only` (the default), `role` or `engine`, spelled exactly so. Any other spelling, a different
  case or a number is `records.document_invalid`. An absent gate means `form_only`, the default DES-0004 section 1
  defines. The canonical body omits an explicit `form_only`, so every existing body, digest and the goal-4 canonical
  literal are byte-identical.
- `creation_role_id`: for a `role` gate only, the one role whose holders create the type's records, a domain role name
  as Records names roles elsewhere. Outside the Author stage a `role` gate without one refuses
  `records.creation_role.required`, and any other gate with one refuses `records.creation_role.unexpected`, both at
  `/creation_role_id`.

Publication resolves a `role` gate's creation role through the `RoleVocabulary` the store was constructed with. With no
vocabulary, or a role it does not define, publication refuses `records.creation_role.unknown` at `/creation_role_id`. A
supplied vocabulary is honoured by `DiagnoseAsync` too. The bind path never re-resolves the role, so a later vocabulary
change cannot make a published type unbindable.

`RecordCreationPolicy.Of(document)` and `RecordTypeDefinitionStore.GetCreationPolicyAsync(tenant, recordTypeId,
version?)` are the one governing policy read. The second reads the published head or a pinned published version and
returns null when it is not published.

`CreateAsync` asks the gate for a typed target: `AccessRecord.RecordTypeId` is the requested Record Type, and the
evidence must name the same type (`hlp.foundation.actor` revision 5). It adds no trace fact.

**Host decider contract (W10), stated on `RecordCreationAuthorization`.** Platform sends exactly one request per call,
before any other stage:

- act `records:write` (`RecordCreationAuthorization.Act`), the principal, the tenant and the act instant;
- record kind `record` (`RecordCreationAuthorization.RecordKind`), the record id minted for the attempt, and the
  requested type as `RecordTypeId`;
- empty `Fields`: no version pin, no creation role and no form.

A conforming adapter reads the type's published head policy through `GetCreationPolicyAsync` (version null). It allows
only when that policy is `role` and the principal holds its `creation_role_id`. It denies `form_only`, `engine`, an
unpublished type and an engine or reserved principal.

So the newest publication's policy governs every create of the type (lead convention E4). A pin to an older version
whose role differs is decided under the head's role: a holder of the head's role creates a record bound to the older
version, and a holder of only the older role is denied.

**L152 (role-gated creation) is met only when a W10 adapter honours this contract.** Platform itself enforces the
type-level rule that needs no verdict. Tests whose trait `Proves` is `w10-decider-contract-fixture` prove the
contract's specification against a fixture decider, not production enforcement.

`CreateAsync` is a principal's direct create. After the gate allows and the type binds, the bound revision's own gate is
enforced before anything is staged, whatever the gate answered and whatever form metadata existed:

- `form_only` refuses `records.write.form_only`;
- `engine` refuses `records.write.engine_only`;
- `role` proceeds on the decider's allow.

Both refusals are in the **bind** stage at `/record_type_id`, because the refusing fact belongs to the bound revision,
not to the principal (ADR 0038 order: authorize, bind, ...). They return `Decision`, which is the authorize stage's own
verdict on the typed act, exactly as `entity.validation.schema_unknown` does at bind. No refusal is ever reported in the
authorize stage beside an allowed decision, so the trace equals enforcement (DES-0032). The two layers together mean that a
pin to an older `role` version cannot widen a newer `form_only` head (the decider refuses), and a pin to a `form_only`
version is refused although the head is `role` (the bound revision refuses). A form that invokes a `role` type's create
uses this same single decision and adds no creation gate (D1). The two refusal spellings are proposed.

### Accepted commands: the form-origin entry (plan W8, ADR-0110 accepted 2026-10-12)

A `form_only` type's records are created only from a durably accepted form submission. The accepting store (the Forms
submission store) commits the exact command, the submit gate's authority snapshot and the delivery row together; the
host's recovery worker then calls `RecordWriteOperation.CompleteAcceptedAsync(RecordWorkerScope, AcceptedRecordCreate)`
while it holds the store's execution claim on the command. In order:

1. **The worker's own act is decided first** (RW-1): act `records:complete-accepted` on record kind `accepted-command`,
   id the command id, typed by the command's Record Type, with the submitter and operation id as fields
   (`AcceptedRecordCommandAuthorization`; the spellings are accepted 2026-10-12, ADR-0110 open item 4). A conforming
   host adapter allows only its recovery worker, only for a command its store holds and has claimed, and never through
   a tenant-wide or root grant (K7). A refused worker learns nothing and the command stays pending.
2. **The submitter is never decided for again.** Creation authorization was decided at acceptance by the form's submit
   gate (ADR-0043), so an ordinary revocation after acceptance does not cancel the command (D4, K2). This is ADR-0110
   decision 1's reading, accepted 2026-10-12, `AcceptedRecordCommandReadings.CreationAuthorizationDecided`.
3. **The binding is validated at runtime.** The worker must act in the command's tenant and must not be the submitter
   (`records.command.binding_mismatch`). The acceptance snapshot must be this command's own acceptance decision
   (`AcceptedRecordCommandReadings.IsAcceptanceEvidence`, revision 2): a gate decision of `forms:submit-gate` on
   `form/{FormRevision}`, allowed, for that tenant and submitter, at exactly `AcceptanceDecidedAt`
   (`records.command.not_accepted`). The body's `records-write-intent/1` digest must equal the `BodyDigest` bound at
   acceptance (`records.command.binding_mismatch`). A command without that evidence is ordinary queued work and goes
   through `CreateAsync`, decided at commit (ADR-0100 decision 3). Bytes alone cannot prove that the accepting store holds
   the command; that residual is escalated.
4. **Idempotency on the original operation.** The key is the command's tenant, the submitter and the original operation
   id, with the fourth frame `accepted-command` (`RecordWriteOperationKey.Origin`). Direct-create keys are byte-unchanged.
   Duplicate delivery and recovery by another worker or a delegate replay the stored receipt and never insert twice;
   another payload under the operation is `records.write.replay_conflict`; a missing or unreadable receipt is
   `records.write.receipt_unsupported` and never authorizes another insertion.
5. **Execution-time invariants.** The head published at execution binds and validates the body. An `engine` head refuses
   `records.write.engine_only`; any other head that is no longer `form_only` refuses
   `records.command.creation_gate_changed` (open item 11 reading: only a form-originated create of a `form_only` type is
   an accepted command).
6. **One atomic commit with provenance.** The row and the audit name the worker as actor and the submitter as
   originator (`RecordWriteRow.OriginatorId`, `KernelAuditEvidence.OriginatorId`), the row links the command
   (`AcceptedCommandId`) and its acceptance audit (`AcceptanceAuditId`), and the authority snapshot is the worker's own
   decision.

`AcceptedRecordCreateOutcomes.IsTerminalRefusal` tells the worker which outcomes end the command: authorize-stage
refusals and `records.write.receipt_unsupported` stay pending, every other refusal is terminal. The three
`records.command.*` spellings are provisional (ADR-0110 open item 7).

This slice does not build Mutate, Rules or React.

## Configuration generations (T-459)

`ConfigurationGeneration.Resolve` identifies a complete host-resolved tenant configuration. Pass active package keys, the full transitive package closure (manifest references, all content references and direct dependency keys), one package ownership selection per distinct content key, the platform contract reference, and all applicable policy references. Empty policy is an explicit host resolution, never an inferred default. Public keys and revisions are case-sensitive; digests are lowercase SHA-256. Duplicate identities, unresolved dependencies, unreachable packages and incomplete or invalid ownership refuse admission.

The digest hashes compact UTF-8 JSON with a versioned domain, fixed property order, ordinal-sorted collections, and no trailing newline. The read document exposes the digest, algorithm and canonical reference snapshot. Inputs are copied before hashing. The host remains responsible for authenticating the caller, authorizing tenant reads, verifying the referenced released content and obtaining one complete effective snapshot. This producer cannot discover omitted active roots or policies from host input, and a digest is not proof of authority or release status. Secret content never belongs in this reference-only DTO.

`ConfigurationGenerationDetail.Definition` ships in ck-7 as `configurationGenerationDetail`; `Bind(generation)` returns the matching public field values. Both runtime lanes consume this definition with host read-only mode enabled. The pinned neutral fixture and both renderer tests distinguish the complete generation digest from constituent package versions. This slice does not install packages, switch effective pointers, implement API routes, or mount app pages.

## Prepared activation contracts (T-460)

Resolve the baseline and candidate through `ConfigurationGeneration.Resolve`, then call
`ConfigurationPreparation.Prepare` with the expected baseline digest and a trusted host projection
validator. It receives those same immutable generations. It must build an isolated immutable
projection and validate every applicable content kind, platform contract, policy, destination data
shape and pinned instance. An empty finding list means explicitly checked and compatible. Missing
validation, incomplete projection references and named projection or compatibility findings refuse;
unknown compatibility must produce a finding. No prepared token is returned on refusal or interruption.
The prepared token binds the baseline, candidate, projection reference and canonical ownership choices.
Preparation neither authorizes activation nor changes the effective generation.

`ConfigurationActivationRequest` adds the server-derived acting principal and evidence intent identity
and reason to that token. `DecideCompareAndSwap` asks the host's live Access callback to authorize that exact request, then compares the complete current baseline again and refuses staleness without retrying. A refused caller gets `configuration-authority-refused` whatever the current generation's state, so it cannot learn whether its baseline is stale (T-1046).
The returned decision binds the request and retained Access decision identity. No independent ownership
list or workflow approval can substitute for those inputs. Host-provided projection validation and
Access callbacks are trusted adapters, not client-supplied assertions.

The API implements `IConfigurationActivationTarget.CompareAndSwapAsync`. Within its transaction it
must verify projection availability and integrity, fence destination compatibility, read the complete
current generation, resolve authority, and atomically commit ownership, the effective pointer, the
Access decision reference and reconstructable evidence intent. The evidence intent identity is scoped
to the tenant and binds the entire decision. The host must refuse reuse with different inputs and use
the committed intent to recover an interrupted acknowledgement. Evidence publication follows through
the committed outbox. Reads and writes pin one generation for their entire unit of work.

A successful pure decision is permission to commit. Only the host's post-commit
`ConfigurationActivationOutcome.ConfirmCommitted` attestation produces an effective result.
`Refused` retains the current effective generation, including when a prepared projection disappears
or destination compatibility changes at the switch. Exceptions propagate without manufacturing an
outcome. A lost commit acknowledgement is indeterminate and requires recovery; it must never be
reported as a refusal or blindly retried. The platform does not implement that durable transaction,
recovery storage, outbox publisher, transport, installation or app pages.

ck-7 exports `configurationActivationDetail` and `configurationActivationStatuses`, the single Form
and released/preparing/refused/effective vocabulary. `ConfigurationActivationDetail` derives read-only
bindings from preparation or the host-confirmed outcome. Even successful preparation displays
Preparing; a refusal cannot be confirmed as committed or bound as Effective. Baseline observations
are point-in-time snapshots: the host refreshes them for current reads. The host verifies release
authenticity before using the Released binding. Both SchemaForm lanes render the same exported Form
against `conformance/hlp.blocks.builder-definitions/activation.json`, and producer tests check every
fixture value. Contract interruption tests cover preparation and switch decisions, not API crash
recovery or durable atomicity.

## Proposed change, Saved version and Released package (T-461)

`ConfigurationProposal` is the propose, save and release half of the governed loop, and it is pure:
the host owns every store. `Start` records the exact effective generation as the proposed change's
baseline. `Autosave` replaces only the named definition's body and preserves the rest of the working
set; an unparseable body refuses rather than being repaired. `WorkingDigest` identifies the exact
state now being edited, and it moves on every edit — that movement is what makes a **Saved version**
and a recorded check distinguishable from the state now in front of the author.

`Save` freezes the working edits into an immutable checkpoint with a required author, a required
rationale and an admitted instant. Its frozen edit list cannot be written through, and a later
autosave leaves it untouched. `ProposedChangeCheck` binds one working digest; the verification engine
and receipt content are the verification slice's, and this producer owns only the binding and its
invalidation through `IsCurrent`.

`ProposedDefinitionEdit` carries the **transport content kind** its definition is, stated by whoever
authored the edit (T-667). That is the whole of the definition-key to content-kind mapping: it is
stated once, per edit, in the block that owns definitions, and a consumer turning a released package
into an installable artifact reads the kind rather than deriving it from the definition key. A
host-side key-to-kind table would be a second place that has to know the set of kinds, so this block
validates only that a kind was stated and deliberately does not enumerate them — a name the transport
does not define is that consumer's named refusal. The stated kind is part of the working digest, so
restating a definition under another kind is an edit like any other and invalidates an earlier check.

`Release` exports exactly the named saved version as one provider-neutral document through the
existing `PlatformPackageExporter`: closure, manifest and digest, validated as replayable before it
is exported. Its own package record carries the baseline generation, the saved version digest and
ordinal, the author, the rationale and the check receipt, so a reader of the bytes alone can tell
what the package was proposed against. `ReleasedPackage.Digest` is the SHA-256 of those exact bytes,
so the digest shown to the author cannot drift from the artifact it names. Release refuses
`configuration-check-invalidated` when the proposed change moved after the check, and
`configuration-baseline-stale`, naming both generations, when the effective generation moved under
the author, and `configuration-check-required` when no check was recorded at all, so a host never
has to author a release rule of its own. **The platform signs nothing**: transport, signing and installation are the api's, per
ADR 0097 decision 6.

The released document's `closure.dependencies` is the package's real closure (DES-0029 ck-2): every package its edits reference, the baseline owner of each edited definition and the package each edit names, except the released package itself, each once as `{key, version}` in ordinal key order and pinned at the revision the baseline generation resolved. The pin is a minimum-inclusive floor, and the closure resolves under `Harborline.Kernel.Core.KernelPackageClosure`. A referenced package the baseline does not resolve has no pin, so Release refuses `configuration-release-dependency-unpinned` naming it rather than dropping the dependency. `PlatformPackageManifest` takes the closure as `PlatformPackageDependency` values and refuses a blank key or version, its own key and a repeated key.

A definition can also name packages in its own envelope (records-ck-41, ADR-0006): Codec-recognized Rule sources, including stored bodies with shared identity metadata omitted, use bare package strings; Layout uses objects whose `capability` is the bare package ID, and Records uses `pack-key@interfaceVersion` objects. Each joins the closure at the baseline's pin like any other referenced package, and refuses `configuration-release-dependency-unpinned` the same way when the baseline does not resolve it. Bare IDs are preserved exactly; no interface version or package revision is inferred from them. Layout's sealed `platform.layout` requirement is a platform capability and adds no package dependency. Non-package Records requirement objects and malformed requirement shapes are ignored, as is an `exposes` member of any other shape. The released document lists only the package's own definitions whose envelopes declare a positive integer `exposes.interface_version` as `exposes`, with that version as `interfaceVersion` (ADR-0028), the names the api manifest already reads. For an existing definition, the complete baseline's canonical ownership selection takes precedence over the edit's stated package; a new key absent from that baseline uses the edit's stated owner. Key prefixes confer no ownership. Foreign narrowing retains its authored body and exact dependency pins, but its exposure declaration contributes neither a manifest key nor an interface version. A package has one interface version, so owned definitions exposed at different ones refuse `configuration-release-interface-ambiguous`. A release exposing nothing writes neither member. The transport carries the baseline digest and each edit's stated package, without an authenticated ownership map; the host supplies the verified effective baseline and the installer must still check installed ownership before export or write. `PlatformPackageManifest` takes the exposure as a `PlatformPackageExposure` and refuses an interface version below 1 and an empty, blank or repeated definition key.

Goal 3 P1 (Option A, approved 2026-10-12; DES-0015 records-ck-40, records-ck-41) adds a `Release` overload that takes a host published-revision resolver (`Func<string, DefinitionRevision?>`). It asks the host only for the package's own exposed definitions, in ordinal key order, and binds each to the exact published revision whose body it is: the revision must be `Published` in the proposal's tenant, of kind `Records`, hold the edit's `BodyJson` byte for byte (ordinal), be catalogued under the top-level `record_type_id` string that body names (its `Document.Key.DefinitionId`, ordinal; review #282 identity), and name the released package key as its envelope `package_id`, which is what a consumer's pin names. Otherwise the release refuses `configuration-release-exposure-unpublished` (no revision, not `Published`, another tenant's, a body naming no `record_type_id` string, or a revision catalogued under another id), `configuration-release-exposure-not-records`, `configuration-release-exposure-body-mismatch` or `configuration-release-exposure-package-mismatch`, naming the definition key, rather than exposing the definition unbound. Records admission makes a Record Type's body id its catalogue key, so a revision passing every check is the definition its own body names; a revision of another definition with a byte-equal body refuses. The exposed key is the baseline-closure name, not a catalogue key, and the block derives no catalogue id from it: mapping the key to the definition it names stays the resolver's (host) duty. Under the Class hold an exposed Class is not a Record Type: it refuses `configuration-release-exposure-not-records`, so a package that exposes a Class cannot be released bound until a Class content kind exists. A bound release writes `exposedDefinitions` right after `interfaceVersion`: one `{definitionKey, version, digest}` per exposed key in ordinal key order, where `version` is the revision's catalogue `Document.Version` and `digest` is `CatalogueFieldSource.DigestOf` of it, the `sha256:` store digest of the stored bytes. These are the facts a consumer's pin carries and the Records install gate compares (`PinnedExposure`). They are never the package revision, a content id or a body member, and the digest is never recomputed from the re-serialized body the definition item carries. An edited definition that is not exposed is never resolved and may still be an unpublished working body. A release exposing nothing asks for nothing and exports the same bytes as the overload without a resolver. That overload binds nothing and is unchanged, so its exposure names keys and an interface version only, and a consumer pin to it refuses `records.reference.closure_missing` at install. `PlatformPackageExposure.ExposedDefinitions` defaults to empty, and an empty list writes no member, so every existing export, including the released platform package 1.0.0, keeps its bytes (K6). `PlatformPackageManifest` refuses `platform-package-exposure-malformed` for an entry naming a key the exposure does not, a repeated key, a blank version, or a digest other than `sha256:` and 64 lowercase hex digits.

ck-7 exports `configurationProposalDetail` and `configurationProposalStatuses`, one released Form and
the domain-facing **Proposed change**, **Saved version** and **Released package** vocabulary. Both
SchemaForm lanes render that exported Form over one shared fixture,
`conformance/hlp.blocks.builder-definitions/proposal.json`, which is the single Records-and-Forms
example — adding a purchase-order number to the invoice Record type and showing it on the invoice
Form — completed identically in React and Blazor rather than twice in two similar shapes. This slice
adds no API route, no signing, no installation, no persistence and no app page.

## Declarative verification: suite, catalogue and receipt (T-463)

`VerificationSuite` is the declarative half of domain verification, and it is pure: the platform
executes nothing. A **fixture** is a controlled starting world — instant, time zone, locale,
identifier seed, collection ordering, actor, the grants that actor actually holds, every registered
port with the deterministic simulator standing in for it, and the seed facts. None of those are
defaulted; a blank one refuses `verification-fixture-input-required`, because a result that depended
on an undeclared input would be coincidental. An **invariant** is one fixture, one action and one
set of assertions. A **parameterized claim** is the same assertions repeated once per examples row,
with the row identity carried into the result so a failure names the row.

`VerificationCatalog` is the closed action and predicate catalogue, and closing it is what keeps the
format declarative. A case can only *name* an action and a predicate, so there is nowhere for a
script, an expression language, a network address or an LLM prompt to go. Each predicate reads one
observation channel and compares it with an expected value of one declared kind. The catalogue has
its own versioned reference, and a receipt must carry it.

**A check containing no meaningful assertion cannot pass**, and that is enforced twice, structurally.
An assertion is *meaningful* when it can fail: it names a registered predicate, it carries an
expected value that is well typed for that predicate's declared kind, and it reads a channel the
case's own action produces. `Declare` refuses anything else by name — `verification-assertion-required`
for a case that asserts nothing, `verification-predicate-unknown`, `verification-expected-invalid`,
`verification-observation-unavailable`, `verification-input-unbound` for a case whose action
parameter is unbound, and `verification-expected-invalid` for an examples row that states no
expectation. `Parse` re-admits a document through `Declare`, so a suite arriving over the wire
cannot carry a case that authoring would have refused; it also refuses a document written against
another catalogue rather than re-deriving its meaning under this one, reads only string members so a
malformed document reaches a named refusal rather than an exception, and treats an instant that does
not parse as an undeclared instant. The second gate is in the result:
`VerificationCaseOutcome.Status` is *derived* from the observations, and there is no constructor,
factory or setter anywhere that produces `Passed` without a matched observation. Nothing observed is
`Vacuous`; `Unsupported` and `Blocked` are results, not skips; and a receipt whose outcomes are not
all `Passed` is not a passing run.

`VerificationReceipt.Mint` produces one immutable receipt or refuses by name. It binds the tenant,
the exact candidate generation, the baseline it was prepared over, the suite reference, one
reference per fixture — derived by the suite itself, not supplied — and every engine identity
including the catalogue. It must answer every declared case and every declared examples row exactly
once, and an outcome that observed anything must have observed every assertion its case declares —
so a run cannot be made green by dropping a case, nor by dropping the one assertion it would have
failed. Observing nothing at all stays legal and stays `Vacuous`. Engine identities are deduplicated
and fully ordered, so the digest depends on the engine set rather than on how the caller assembled
the list, and a list repeating the catalogue is not a second engine. `ReceiptId` is what `ProposedChangeCheck`
binds, which is the seam [[T-461]] left for this slice. `Digest` is the SHA-256 of the canonical
receipt document, so the receipt is replayable rather than a colour.

ck-7 exports `verificationRunDetail`, `verificationRunStatuses` and `verificationCatalogue`, one
released Form and the **Passed**, **Failed**, **Nothing was checked**, **Not supported** and
**Blocked** vocabulary. Both SchemaForm lanes render that exported Form over one shared fixture,
`conformance/hlp.blocks.builder-definitions/verification.json` — the single Records-and-Rules
example, an invoice whose total is a business rule and whose approved status is an authorization
rule — with the canonical suite document both app lanes author and run, the refusal an empty case
earns, and three runs: clean, one business-rule defect and one authorization defect, each failing
its own claim and not the other.

**This slice adds no execution.** Isolated execution through the production interpreters, the
ephemeral tenant context, receipt persistence and transport are the api's, per R-0091's authority
table and ADR 0096 decision 3, under which producers are built in the platform and the api consumes
the released feed; the app pages are the app's.

`IDefinitionKeyAuthority` remains intentionally unimplemented until allocation authority is ruled. This package owns no API transport, signing, installation, Pilot bridge, or UI renderer.
## Released Layout registers

`LayoutFieldControlRegistry.Released` exposes the existing `text` and `currency` controls from
metadata embedded in this library. The first bounded release carries these two controls; other
schema-form hints do not become authored Layout controls merely because a lane can render them.
Text takes no parameters. Currency accepts `decimals` (integer), `currencyCode` (string), `min`
and `max` (number); other names, including `precision`, refuse at admission. The ids and parameter
names are checked against both schema-form lane implementations.

`LayoutValidationRuleRegistry.Released` is empty while the installed Rules catalogue supplies no
published Validate-action rules. A host can construct a register from its admitted closure using
the existing constructor. The release invents no named rule for capture's `required` property.
Hosts supply these registers to the existing `LayoutHostRegisters` seam; API adoption is T-1013.

## Durable Records write store

`FileJournalRecordWriteStore` implements the prepared transaction port, receipt reader and committed
record read port over the same journal. It is composed into no host. Its source writes the record,
operation identity, receipt and opaque audit evidence into a completed temporary file, flushes and
validates that entire candidate, then replaces the journal. The writer gate is process-wide across
instances and paths; it supplies no cross-process exclusion. Native qualification remains owed.

`records-write-file-journal/2` retains a contiguous prefix of raw legacy `/1` entries and appends
bound `/2` entries. The first temporal Created converts `/1` or `/2` directly to
`records-write-file-journal/3`, retaining the raw legacy prefix and bound entries and appending
`temporal/3` entries. Ordinary and accepted Created writes on `/3` preserve that format and every
existing temporal span before appending an unchanged bound entry. Open, read, replay and conflict
do not convert; only a new Created atomically converts and appends. Whole-journal validation checks
the versioned eight-field binding and all seven receipt fields, and refuses corruption anywhere
without a `/1` fallback. Legacy provenance and raw spans remain legacy. The binding hash supplies
integrity evidence, not authentication or valid-downgrade detection.

Current, effective, history and amendment-target reads share the existing async 16 MiB bound;
constructor, receipt and commit loads remain uncapped. Even target metadata can refuse an oversized
journal before authorization or replay. Legacy `/1` retains JSON depth 64; `/2` and `/3` allow 65 for
the retained legacy wrapper, and the writer validates its complete temporary output before replacement. Cancellation and I/O errors keep their distinct
behavior. These source limits and replacement behavior require qualification on the final head.

A bound row's nullable init-only `PublicationVersionId` identifies its exact immutable Published
revision. Current read resolves that identity, verifies tenant/type, label, exact body digest and
stored schema, and preserves the stored opaque schema id. Warm and cold schema binding register
current first, try legacy only after a successful nonmatch, and stop on match, failure or
cancellation; at most two registrations are permitted. A legacy row retains its exact version
lookup and identical-body identity limitation. No current-head fallback is supplied.

Before first `/2` write, stop and drain all `/1` writers and fence their restart sources. Before
first `/3` write, do the same for every writer unable to preserve `/3`, including a `/2` writer
loaded before conversion. A newly opened old reader's refusal does not prove that exclusion. Preserve
a read-only backup/hash and stray temporary files, use one qualified writer, and verify the complete
converted prefix, original receipts, exact replay and later ordinary preservation before reopening
traffic. Restoring `/1` or `/2` requires explicit reconciliation of newer events and operation
identities. This source contract grants no operational cutover or host enablement.

Ordinary retry settles from the committed operation receipt. The replacement has no Unix
parent-directory fsync or Windows write-through, so it supplies no power-loss durability guarantee.
A missing directory reads as empty and commit recreates it, retaining the existing behavior and
owner/T619 boundary. External Windows handles can refuse replacement. Cross-process races and
production-adapter durability remain separate qualification and T619 work.

## Opt-in temporal Records library (T-617, unqualified)

`RecordWriteOperation.CreateEffectiveAsync` creates a new logical record with an explicit
`EffectiveFrom` and nullable exclusive `EffectiveTo`; `AmendAsync` appends an immutable revision
with a required reason and exact `RecordTemporalBasis` revision/hash. No existing row is retrofitted.
The half-open UTC interval requires an end later than its start. Later causal revisions overlay
that interval and retain unaffected segments and gaps; history follows causal revision order,
not authority time. The amendment basis is the latest causal head, which can differ from the
revision selected by an effective-time read. A stale basis returns `BasisMismatch` without writing.

The explicit constructor takes temporal prepared, receipt and read ports on the same object as
the ordinary ports. Existing composition returns `TemporalStoreUnavailable` for temporal writes.
A new Created requires an explicitly configured journal authority clock; an exact accepted retry
can replay without a fresh stamp. `AuthorityRecordedAt` is sampled inside the commit critical
section after replay and CAS, and differs from the unchanged gate-act `Receipt.RecordedAt`.
The caller supplies every interval/AsOf value; this library supplies no request-JSON adapter.

Validate and detach the request first. Amendment uses bounded same-journal target metadata to
establish its tenant/type before current `records:write` authorization. Only after current access
may an exact operation-key/intent retry return its original detached receipt, without rechecking
publication, temporal policy, basis or head. Unsupported receipts and conflicting intents retain
the existing refusal codes. A genuinely new write selects one admitted immutable publication,
requires the explicit temporal policy and validates existing schema/field/domain constraints;
only creation uses the creation-only gate. The prepared transaction stages the row, operation,
receipt and audit together. Locked replay precedes exact-head CAS and clock allocation; complete
finished-output validation precedes one atomic replacement. Refusal publishes no partial event,
audit or stamp; registry side effects can remain. No sidecar journal or post-commit audit is added.

`RecordReadOperation.ReadAsync` selects the effective revision at its single sampled current
instant on a temporal-capable store; an old-only store retains its old `FindAsync` path.
`ReadAtAsync` uses the supplied AsOf for selection and a separately sampled current instant for
access. `ReadHistoryAsync` authorizes every causal revision at one current act instant and returns
all detached results or no bodies, prefix or count. Each selected row binds its own exact immutable
publication and current-first stored-schema sequence. No historical grant is reconstructed.
Explicit temporal reads distinguish `NoEffectiveRevision` and `UnsupportedLegacyBasis`; the old
current read retains its public miss semantics. Direct untimed journal `FindAsync` on a temporal
identity requires an explicit authority clock; effective/history reads need no implicit clock.

All current/effective/history/target, constructor, receipt and commit routes validate the whole
local journal before returning data. Missing owned members, malformed unrelated entries and broken
receipt/binding/chain evidence refuse as existing journal corruption; dependency, I/O and
cancellation exceptions retain their classes. Binding hashes are integrity evidence, not authority.
The existing test controls, fault distinctions and old-writer fencing require final-head proof.
This is an additive producer library, with no host enablement/cutover, accepted-command or Forms
temporal adoption, broad queries/paging, historical grants, retention/legal hold, offline policy,
cross-process/power-loss durability or full T-617/M11 qualification. T-616 literal defaults remain
outside this implementation slice.
