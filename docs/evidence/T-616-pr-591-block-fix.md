# T-616 / PR 591 BLOCK fix qualification

Base: `3eacedbec2775913fdb82f034260288d19def47d`. Qualification: 2026-11-27, Windows PC,
SDK 11.0.100 (exact global.json match), locked restore successful.

Option (b): restore the bounded producer/consumer test and both original caveats. Remove the
test-owned fence journal and its self-checks. Restore the real built-in store's successful claim
and unsuccessful Pending-only refusal assertions, for both in-memory and file-journal stores.
Host retention conformance remains with T-619/acceptance-revocation-fence.

Option (a) is unsound for this fixture: a valid fenced claim must supply no winning revocations.
Returning a later-committed revocation with an earlier audit timestamp would make the legacy
timestamp filter refuse that valid claim, rather than prove ADR-0110's commit-order invariant.
The skew inputs here are refusal-audit timestamps, not proof of the legacy filter or host ordering.

Oracle: ADR-0110 open items 2 and 6 supply the claim-first / refusal-first lifecycle outcomes;
the public store contract supplies Changed=true for the winning claim and Changed=false for
the losing Pending-only refusal. Frozen interface revision 16 supplies the six audit members.
Literal lifecycle states, worker identity, refusal code, zero/one effect counts and literal
ADR-0068 authority snapshot bytes are asserted against production execution. Receipt/outbox
identities bind the audit to the submitted operation. No fixture output proves host retention.

Reuse: routine test repair against the existing stores, public consumer and harness; no new
production mechanism or dependency. Checked the T-616 handoff slice, ADR-0110 items 2/6,
InMemoryFormSubmissionStore claim/refuse paths and AcceptedRecordCommands.RevocationRefusalAsync
at the base revision. Reuse the earlier forwarding refusal fixture and empty lookup input.

## Hand mutant

At `InMemoryFormSubmissionStore.cs:299`, remove the expected-state guard in
RefuseAcceptedCommandAsync. The restored Assert.False at test line 69 kills it:

- `Harborline.Foundation.Forms.Engine.Tests.FormAcceptedCommandFenceHandoffTests.ProducerDecisionAndConsumerOutcomeSurviveAuditClockOffsetsAndRestart(journal: False, revocationFirst: False, auditSkewMinutes: -60)`
- `Harborline.Foundation.Forms.Engine.Tests.FormAcceptedCommandFenceHandoffTests.ProducerDecisionAndConsumerOutcomeSurviveAuditClockOffsetsAndRestart(journal: False, revocationFirst: False, auditSkewMinutes: 60)`

Red: 7 passed, 2 failed, 0 skipped. Restore the production file byte-identically, SHA-256
`80b909b286da480e274ad1d41329257a696a98eacb51af588a80d5ca9a182295`.
The first post-copy incremental run reused the mutant binary because Copy-Item retained the
old timestamp (7 passed, 2 failed). Advance only the source timestamp to rebuild; the bytes
remain identical. Rebuilt green: 9 passed, 0 failed, 0 skipped.
No timestamp-filter mutation qualification is claimed under option (b).

## Runs

Artifacts under `%TEMP%\harborline\pc-591-fix-20261127T114737Z`:

- `focused-green.trx`: focused class, 9 passed, 0 failed, 0 skipped.
- `refusal-guard-mutant-red.trx`: 7 passed, 2 failed, 0 skipped.
- `refusal-guard-restored-green.trx`: stale mutant binary, 7 passed, 2 failed, 0 skipped.
- `refusal-guard-restored-rebuilt-green.trx`: byte-identical source rebuilt, 9 passed, 0 failed, 0 skipped.
- `forms-engine-full-green.trx`: whole forms-engine project, 687 passed, 0 failed, 0 skipped.

All test runs use `dotnet test` with `--no-restore`, TRX logging and the scratch results directory;
focused runs filter `FullyQualifiedName~FormAcceptedCommandFenceHandoffTests`.
TEMP and TMP for the test processes point to that scratch directory.

Provenance refreshed with `node tooling/refresh-provenance-hashes.mjs --module hlp.foundation.forms-engine`.
The same command with `--check` reports `1 record(s) read, 0 stale hash(es)`.
`git diff --check` passes. No production source change remains.
