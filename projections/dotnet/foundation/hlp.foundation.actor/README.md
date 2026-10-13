# Harborline.Foundation.Authorization local shadow

This package contains revision 1 of `hlp.foundation.actor`: current-user identity, a narrow
same-authentication-scope actor/tenant context, and fail-closed server-derived Party resolution.

Authorization policy, OIDC/Okta validation, session storage, People persistence, principal kind,
dependency-injection registration, and production mapping implementations remain outside this
bounded module. The package is a local, distribution-blocked shadow; the original capture retained aggregate
contract identity authority under the earlier repository name; see ticket 063.
# Access filtering and stored trace contracts

The authorization assembly also supplies `AccessProvider` and `AuthorizationTraceReader`. A host implements `IAuthorizationDecider` by forwarding to its existing
authorization gate and translating that decision's evidence. This port carries the verdict; none
of the platform providers resolves grants, intersects bindings, or calculates a second verdict.

Bind the set predicate with the operation, principal, tenant, record kind and query instant. Apply
`FilterAsync` to the complete tenant-bound candidate set before counting, grouping, summing,
exporting or paging. Views uses `AccessViewFilter` to adapt this same predicate to its row-source
plan. A durable row source must preserve that order; the reference row source demonstrates it.

Call `ValidateAsync` from the write validation stage with its predicate instant and validation
callback. It makes a fresh row check before validation and returns the gate's stable refusal.
Commit only after validation succeeds inside the host's transaction. A stage-one allow is never
an input to this method and cannot bypass the check.

`CheckWithEvidenceAsync` is the same fresh row check returned with the decider's own
`AuthorizationDecisionEvidence`, for a writer that must keep the enforcing decision's trace
(RW-1) without asking the gate twice. The decider is called once; an invalid context never
reaches it, and evidence for any other principal, tenant, instant, operation or record is
withheld and refuses `access.decision_mismatch`. `CheckAsync` is this check reduced to its result.

A request may name a typed Records target, `AccessRecord.RecordTypeId` (revision 5, plan W9). It
names the Record Type a record belongs to or is about to be created as, so the decider can decide
an act on one type: for a create, the type's creation policy, including the one `creation_role_id`
of a `role`-gated type (decision D1). It is part of the context. A blank one is
`access.context_invalid` and never reaches the decider. Evidence naming another type, or dropping
or adding one, is `access.decision_mismatch`. The evidence copies it as
`AuthorizationDecisionEvidence.RecordTypeId` and adds no trace fact, so the four steps and their
stored bytes are the same as for an untyped request. Existing requests set no type and are unchanged.

`AuthoritySnapshotEncoding` (revision 6) is the one byte grammar of a stored authority snapshot: the
evidence's tenant, principal, version and four projected steps, exactly as Records has committed them
since T-616 003c. A writer that persists a decision's snapshot atomically with its outcome encodes it
with `Encode`; another module reads it back with `TryDecode`, which returns the stored
`AuthorizationTraceSnapshot` or null for anything that is not exactly that grammar, and `Fact` returns
one recorded fact such as `verdict:allowed`. Nothing is re-derived and no verdict is calculated; a
snapshot is evidence for a reader and must not be disclosed to clients (D6).

Persist `AuthorizationDecisionEvidence.Project()` and the original counterfactual when the host
records its decision. The storage adapter returns only `AuthorizationTraceSnapshot`, not audit
payloads, signatures or diagnostic secrets. The reader authorizes the current `audit:trace-read`
or `audit:read` act and returns the stored four steps. Missing decisions remain absent and guard
refusals remain distinct. Historical acts are never replayed.
Adapters supply the original scoped act and target strings, all roster/attenuation facts, and
the host's principal equality comparer when its identity space requires one (for example OS-user
principals). The default comparer treats canonical opaque identities ordinally.

The evidence role/standing fields, four-stage projection, counterfactual snapshot fields and
read outcomes are promoted from the existing API evidence and trace-reader contracts. Roster,
attenuation, deciding-binding and refusal classification remain gate-computed facts. Host audit
serialization and HTTP adapters remain outside this package. Public distribution remains
unauthorized.
