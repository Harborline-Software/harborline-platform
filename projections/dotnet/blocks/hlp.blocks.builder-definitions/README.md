# Harborline Blocks Builder Definitions

Local-shadow .NET package for the shared definition catalogue substrate. It provides advisory key suggestion, tenant-scoped durable archive lifecycle, and a projection-neutral platform package manifest with deterministic JSON export and validated replay into an empty catalogue.

`PlatformPackageContent.Unresolved(reference)` keeps DES-0007 policy gaps explicit. Validation refuses unresolved content rather than choosing a default. `PlatformPackageReplayer` validates the complete manifest before one atomic apply-to-empty call, and preserves declared bootstrap order in that batch. The target owns the atomic all-or-none commit and non-empty check. Stable refusal codes cover empty manifests, a missing or repeated first package record, non-empty targets, duplicate ids, unknown or reordered stages, missing or forward dependencies, and unresolved content.

`PlatformPackageSeed` is the ADR-0097 fixture for `platform-package-ck-1` through `-ck-14`. Its checked-in `_shared/packs/platform/platform-pack.export.json` is embedded in and packed with the assembly; `VerifyCheckedInExport` proves it is byte-identical to the provider-neutral closure, manifest, and SHA-256 digest document produced by the exporter. The fixture does not choose the still-open first-authority source or preloaded Access-pack provenance kind.

`PackageSafetyFloorReattachment` owns the released raise-only rule for a reattached package candidate. It restores a missing `safetyFloors` object or seeded member, clamps a lower integer to the seed value, and refuses `platform-package-safety-floor-malformed` with the member name when a seeded floor is present as a non-integer. It returns an independent content snapshot and never mutates either input.

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

`IDefinitionKeyAuthority` remains intentionally unimplemented until allocation authority is ruled. This package owns no API transport, signing, installation, Pilot bridge, or UI renderer.
