# `@harborline-software/copilot-contracts`

Provider-neutral Pilot proposal contracts: fail-closed envelope parsing, AP/CP/NEVER classification, manifest formatting, stream extraction, bounded context vocabulary, and pure surface descriptors. This package contains no UI, network, storage, host globals, or live executors.

Applications implement `PilotEffectAdapter`; see the ticket draft’s top-level `adapter-contract.md` for trust and crossing rules.

## Delegation wire shapes (T-610)

`serializeProposalCanonical(ProposalEnvelope)` emits compact JSON with RFC 8785 ordinal UTF-16 key ordering at every depth, unchanged array order and no newline. `await proposalDigest(proposal)` uses Web Crypto SHA-256 over its UTF-8 bytes and returns `sha256:` plus lowercase hex. The existing `ProposalEnvelope` type is unchanged.

`parseDelegationRecord(unknown)`, `parseDelegationDisposition(unknown)` and `parseStandingApproval(unknown)` refuse missing, extra or invalid members and return detached wire values:

- `DelegationRecord`: `{subject, actor, disposition}`. Actor is null for a hand-performed act, otherwise exactly `{binding_key, binding_version, provider_id, model_version, proposal_digest}`. No proposal content or context is an actor member.
- `DelegationDisposition`: `{kind: approve|dismiss|clarify|reject}` or `{kind: standing, approval_id, approver, scope, expires_at}`. This human/audit vocabulary is not the classifier's existing `ProposalDisposition` (`auto-apply`, `card`, `reject`, `clarify`).
- `StandingApproval`: `{id, approver, binding_key, scope: {surface, commands[]}, schedule, expires_at, revoked_at}`. Approver and a nonempty command scope are required. UTC instants use `Z` or `+00:00` and up to seven fractional digits. Expiry is required; revocation may be null. Schedule is an RRULE string or null, preserved without expansion or recurrence-grammar admission.

These wire shapes grant no authority, serialize no dispatch receipt and travel in no Assistance definition pack. T-613 owns audit writing and act-time expiry/revocation coverage. The same `conformance/hlp.copilot/delegation-cases.json` is read by this package and .NET, with literal canonical bytes and independent `shasum -a 256` digests. Its digest corpus covers integer arguments; exhaustive non-integer RFC 8785 number agreement remains the slice record's follow-up.

