# Harborline.Foundation.Authorization local shadow

This package contains revision 1 of `hlp.foundation.actor`: current-user identity, a narrow
same-authentication-scope actor/tenant context, and fail-closed server-derived Party resolution.

Authorization policy, OIDC/Okta validation, session storage, People persistence, principal kind,
dependency-injection registration, and production mapping implementations remain outside this
bounded module. The package is a local, distribution-blocked shadow; the original capture retained aggregate
contract identity authority under the earlier repository name; see ticket 063.
# Access filtering and stored trace contracts

The authorization assembly also supplies `AccessProvider`, `AccessScopeEvaluator`, and
`AuthorizationTraceReader`. A host implements `IAuthorizationDecider` by forwarding to its existing
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

Scope evaluation receives an `AccessAuthorityContext` from the trusted authority-admission host,
bound to exactly one principal, tenant, record and instant. Do not deserialize that context from
client input. Declare every `principal`, `tenant`, `instant` or `record.*` reference. Missing or
mismatched authority and undeclared references refuse before the shared rule evaluator runs.
Scope evaluation supplies a scope fact to the existing gate, never an authorization verdict.

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
