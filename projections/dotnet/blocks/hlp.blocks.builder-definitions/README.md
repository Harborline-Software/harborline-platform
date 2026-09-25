# Harborline Blocks Builder Definitions

Local-shadow .NET package for the shared definition catalogue substrate. It provides advisory key suggestion, tenant-scoped durable archive lifecycle, and a projection-neutral platform package manifest with deterministic JSON export and validated replay into an empty catalogue.

`PlatformPackageContent.Unresolved(reference)` keeps DES-0007 policy gaps explicit. Validation refuses unresolved content rather than choosing a default. `PlatformPackageReplayer` validates the complete manifest before one atomic apply-to-empty call, and preserves declared bootstrap order in that batch. The target owns the atomic all-or-none commit and non-empty check. Stable refusal codes cover empty manifests, a missing or repeated first package record, non-empty targets, duplicate ids, unknown or reordered stages, missing or forward dependencies, and unresolved content.

`PlatformPackageSeed` is the ADR-0097 fixture for `platform-package-ck-1` through `-ck-14`. Its checked-in `_shared/packs/platform/platform-pack.export.json` is embedded in and packed with the assembly; `VerifyCheckedInExport` proves it is byte-identical to the provider-neutral closure, manifest, and SHA-256 digest document produced by the exporter. The fixture does not choose the still-open first-authority source or preloaded Access-pack provenance kind.

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
and reason to that token. `DecideCompareAndSwap` compares the complete current baseline again, refuses
staleness without retrying, and asks the host's live Access callback to authorize that exact request.
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
