# T-616 / PR 591 BLOCK fix qualification

Candidate: this commit, based on `a7a710b25c9d915a8278380965e383b918a96f18`. The built-in stores demonstrate bounded producer lifecycle persistence; the public consumer tests demonstrate its response to supplied decisions. Host retention and the shared database fence remain for T-619 qualification.

Oracle: ADR-0110 items 2 and 6 and the public store contract supply lifecycle outcomes and Changed semantics. Literal states, audit fields, refusal codes, zero/one effect counts and authority snapshot bytes are checked against production execution. The new legacy selection test uses literal Compromise at Now-2, TenantSuspension at Now-1, and the independent literal `records.command.revoked_compromise`, with zero effects. It does not prove host ordering or retention.

Reuse: based on a7a710b2, checked the interface revision 24, ADR-0110, existing built-in stores, public consumer and existing tests, plus docs/engineering-reuse.md. Reuse the existing harness and store transitions; this routine test repair adds no production mechanism or dependency. The host must supply the shared fence and retained decision.

## Evidence

Windows SDK 11.0.100, locked restore. The new `Harborline.Foundation.Forms.Engine.Tests.FormAcceptedRecordCommandTests.EarliestNonOrdinaryRevocationDeterminesRefusalCode` fails under M4 (ascending changed to descending) and passes after byte-identical restoration. Focused: 119 passed; whole forms-engine: 688 passed. Receipts: `%TEMP%\harborline\pc-591-fix2-20261127`.

At previous head a7a710b2, review receipts `%TEMP%\harborline\pc-591-tb-sol-20261127T120602Z` establish:

- M1 (`<=` to `<`): killed by `FormAcceptedRecordCommandTests.NonOrdinaryRevocationBeforeTheClaimRefusesTheCommand(Compromise,0,"records.command.revoked_compromise")`.
- M2 (claim Changed false to true): killed by `FormAcceptedRecordCommandTests.RevocationFoundOnAResumedClaimQuarantinesAndTheRecordStands` and `TerminalRefusalAfterAResumedClaimIsQuarantined`.
- M3 (remove refusal expected-state guard): killed by `FormAcceptedCommandFenceHandoffTests.ProducerDecisionAndConsumerOutcomeSurviveAuditClockOffsetsAndRestart` (in-memory claim-first, both audit offsets) and `FormAcceptedRecordCommandTests.RefusalWithAStaleExpectedStateChangesNothing`.
- M4 (descending revocation order): killed by the new test on this candidate.

The Mac mini gate at a7a710b2 passed 23/23 (receipts `mini-591-gate-20261127T120638Z`); this new head needs a fresh gate. The 3eacedbe and 92d915ac fixture, gate and mutant results are historical, not qualification of this candidate.
