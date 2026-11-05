# Harborline.Foundation.ContentStore

The content store substrate ([DES-0054](https://github.com/Harborline-Software/harborline-control/blob/main/designs/DES-0054-content-store/design.md), T-521 slice 2): immutable bytes behind tenant-keyed `hmac-sha256:<hex>` content ids.

- **Put** streams bytes into a staged item, refusing at a named stage: `content.too-large`, `content.media-type-not-allowed`, `content.media-type-mismatch`, `content.digest-mismatch`. Size and quota refusals carry `limit` and `observed` in the DES-0014 C3 `detail`.
- **Commit** turns a staged item into a reference inside one transaction. The backing decides the record and tenant quotas, counted in logical bytes, and the duplicate within the tenant.
- **ReadAsync** authorizes before range evaluation, then returns a segment-streaming body. A first-segment or data-key failure is a typed `ContentIntegrityException` before a host writes headers; a later failure escapes the body so the host aborts it. Delivery uses `filename*` (RFC 8187 UTF-8) after an ASCII `filename` fallback where needed.
- **Erasure** destroys the item's own data key. Each item is sealed in fixed 65,536-byte AES-256-GCM segments; the item records that size. HKDF-SHA-256 derives separate tenant subkeys for content ids and data-key wrapping.
- **Ports.** `IContentBookkeeping` owns items and their wrapped keys, owners, staged items, quota counts, reclamation entries and integrity episodes, with no ciphertext. `IContentByteStore` owns the ciphertext segments. `IContentStoreStorage` hands the store the pair (below). `InMemoryContentStoreStorage` backs both; a host supplies durable storage. `IContentStoreQuotaBase` declares the host-owned Q22 volume measurement.
- **Integrity telemetry.** Every failed read marks `content.integrity` on an Activity span and increments the zero-alarm counter. The optional `IContentIntegrityLogger` receives one structured first-failure event per episode; the backing retains count and last-seen state until a full verified read or erasure.
- **The staging sweep** is registered as `content-staging-sweep`, triggered by `schedule` on `sys.sched.content-staging`.

The item limit, quotas and staging expiry have no defaults. DES-0054 assigns them to the first DES-0037 measurement, so an installation must supply them; segment size is the separately ruled store constant.

## T-521/content-store acceptance reconciliation

Baseline: platform main `7b42ba0a0fceaacee86fa5637afa7ece15664d9c`, control main
`6e3b899d`. Test names below are in the adjacent `hlp.foundation.content-store.tests/ContentStoreTests.cs`.
This is producer evidence, not completion of the durable-host or design-current slices.

| Producer acceptance | Main evidence | Remaining at reconciliation |
|---|---|---|
| Registered sweep reclaims abandoned staging without a request | `ContentStoreRunRegistration.Register`, `SweepExpired`; `Clause_ck13_a_staged_item_nobody_commits_expires_under_the_registered_sweep_with_no_committed_orphan` | Already implemented; rerun the test. Scheduling execution belongs to the host. |
| One byte over the effective limit refuses with limit/observed and nothing staged | `Put`; `Clause_ck7_eng1_put_one_byte_over_the_effective_limit_refuses_too_large_with_detail_and_stages_nothing` | Already implemented for small uploads; add a segment-boundary refusal test to prove previously sealed segments are not retained. |
| Duplicate put stages its own copy; commit has the same response | `Put`, `Commit`; `Clause_ck4_eng4_same_bytes_twice_in_one_tenant_store_one_item_and_in_two_tenants_two_items` | Response and tenant isolation are covered. Dedup timing must be measured on the durable backing, not this in-memory implementation (Q31). |
| Export fixity verifies every keyed digest and refuses tampering without plain SHA-256 | `Export`, `VerifyExport`; `Clause_cc7_export_fixity_verifies_every_keyed_digest_a_tampered_byte_refuses_and_no_plain_sha256_is_carried`, `Clause_cc7_swapping_two_exported_content_ids_fails_fixity` | Already implemented; rerun both tests. |
| PR 232 follow-up Q27–Q33 | Fixed segment size and separate HKDF labels in `ContentStore.cs`; `Clause_q27_q28_fixed_segments_and_purpose_bound_hkdf_subkeys_keep_content_ids_and_wraps_separate`, sweep test above, `Clause_eng7_a_served_item_carries_nosniff_attachment_a_strong_etag_and_only_the_detected_type` | Producer constants, ports and RFC 8187 output exist. Host persistence and quota measurement are separate. |
| Q30 read failures do not expose content codes; authorization precedes ranges | `Readable`, `Read`; `Clause_eng6_eng11_an_unauthorized_read_and_another_tenants_read_refuse_exactly_as_a_nonexistent_id`, `Clause_q30_unauthorized_range_is_indistinguishable_from_not_found_and_never_reveals_a_416_or_content_range` | Producer outcomes exist. Identical HTTP 404s, empty 500s and protocol aborts require the host. |
| Q34–Q36 durable integrity episodes, recovery, telemetry and no partial-range clearance | `ReportIntegrity`, `ResolveIntegrity`, `IntegrityEntries`, `IsIntegrityHealthy`; `Clause_q30_q34_q36_middle_segment_range_failure_is_typed_logged_once_counted_and_only_a_full_verified_read_ends_the_episode`, `Clause_q30_q36_every_failed_read_marks_an_error_span_with_error_type_and_increments_the_zero_alarm_counter`, `ContentIntegrityTelemetryPrivacyTests` | In-memory behavior exists. Add restart coverage for the episode. Request-id headers, audit authorization and HTTP health exposure belong to the host. |
| Segment-streaming delivery, typed middle-segment/range and wrap-key failures, tested mid-stream abort | `ContentReadStream`; middle-range test above, `Contract_range_math_wrap_corruption_and_multiple_integrity_episodes_remain_observable` | Streaming and typed exceptions exist. Add a direct read test proving the first segment is delivered before a later fault, and that retrying a failed body cannot skip a corrupt segment or complete normally. |
| Storage and quota-base ports; shared section 9 and restart tests | `IContentStoreStorage`, `InMemoryContentStoreStorage`, `IContentStoreQuotaBase`; `ContentStoreStorageContract.Clause_q31_storage_port_preserves_committed_staged_and_reclamation_state_when_a_store_is_rebuilt` | Ports exist. Only the restart test accepts a storage factory; the section 9 tests construct in-memory storage directly. Make the producer suite reusable and provide a restart hook for closing/reopening a durable backing. |

Pack installation (section 9 ck-15), actual HTTP delivery, durable transactions, the Q22
volume adapter and DES-0037 measurements are consumer/host work, not substitutes for
the producer checks here. No default limits, new dependencies or alternative storage
implementation are introduced by this slice.

### Producer follow-up

`Clause_q30_a_failed_body_never_skips_a_corrupt_middle_segment_or_completes_normally`
reproduces a main defect: after a middle-segment failure, another read could skip
that segment and return bytes. The body now retains its typed failure and cannot
resume, including on an empty-buffer read. Its decrypted buffers and data key are
cleared on failure/disposal; a disposed body refuses reads without opening an
integrity episode. `Length` remains the delivery length after consumption, and a
cancelled asynchronous read leaves the body and integrity state unchanged.

The segment-boundary limit test and
`Clause_q34_storage_restart_preserves_integrity_until_full_verification_or_erasure`
cover the other new producer evidence. The latter also checks that an active
episode, repeat count and last-seen time survive reconstruction, a partial range
does not clear it, verified recovery persists, and later corruption logs anew.

To run the shared producer conformance suite against another backing, derive a
concrete xUnit test class from `ContentStoreTests` and override `CreateStorage` to
return a fresh, isolated backing on **every call**. Override `RestartStorage` to
close and reopen that backing without deleting its persisted state. The default
restart hook retains the same in-memory backing; it proves producer reconstruction,
not process/disk durability. `InMemoryContentStoreStorageContract` runs the complete
inherited suite. HTTP protocol tests and durable timing evidence still belong to
the durable-host slice.

## Writes through the kernel boundary (T-521/content-store-host-transaction)

By the owner's ruling of 2026-11-05 (17:47 UTC), every store write carries its own actor and audit. `PutAsync`, `CommitReferenceAsync`, `RemoveReferenceAsync` and `SweepExpiredAsync` take a `ContentStoreWrite`: the caller's `KernelOperationIdentity`, its `KernelAuditEvidence`, and optionally the caller's open transaction.

- **With the caller's transaction** the write runs through `KernelTransactionBoundary.JoinAsync`. It stages its own operation, writes and audit row, and never commits or rolls back. Any failure dooms the caller's execution, even one the caller catches, so half a write never commits. A transaction with no open execution is refused.
- **Without one** it runs its own `ExecutePreparedAsync`. Inside an open execution that is refused as `kernel.nested-execution`, so a write can't commit apart from the caller who forgot to pass the transaction.
- **Actors.** The store never invents one. Evidence without a whole identity and actor is refused before any byte is kept. The sweep acts as `sys.scheduler` and refuses any other actor.
- **A write that changes nothing** commits nothing and writes no audit row.
- **Async.** These calls and the ports behind them are async, with no sync-over-async.

## Reads and integrity episodes

By the owner's ruling of 2026-11-05 (22:11 UTC), a failed integrity check writes an audit entry, so reads are async and each needs a named reader.

- **`ReadAsync` and `ExportAsync` require a `ContentReadContext`.** A missing reader or a blank `PrincipalId` is refused before any byte is read, so it can never take the place of a `ContentIntegrityException`.
- **The first covering segment is verified inside `ReadAsync`,** before a host writes headers. The body reads through `ReadAsync` and `CopyToAsync` only; its synchronous `Read`, `ReadByte` and `CopyTo` throw `NotSupportedException`. Kestrel's own default suits this: [`KestrelServerOptions.AllowSynchronousIO`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.core.kestrelserveroptions.allowsynchronousio) "Defaults to false."
- **Each episode write goes through the kernel boundary with the reader as actor** and writes its own audit row. The row names no tenant or content id (Q36). Closing an episode through erasure commits inside the erasure's own write.
- **An episode always commits in its own transaction,** so a caller's rollback cannot erase it (Q34). The boundary refuses a nested execution, so `ReadAsync` is refused as `kernel.nested-execution` inside an open execution, before any byte is read. A body opened earlier and read inside an execution surfaces that same refusal if it needs to write an episode, and keeps its typed failure for later reads; a later read outside the execution opens the episode.
- **A failed episode write surfaces as itself,** never as tampering.
- **Cancelling a read does not cancel its episode write.** A read returns what it has copied before fetching another segment, so a fetch that is cancelled loses nothing and a later read resumes on that segment.

## The two storage ports

The storage port is split by [ADR 0113](https://github.com/Harborline-Software/harborline-control/blob/main/adrs/ADR-0113-content-store-bookkeeping-lives-in-the-node-model-and-commits-with-the-record/adr.md). Bookkeeping commits in a transaction; bytes are written outside it.

**The backing decides, not the store.** The store holds no lock and no copy of the state. `IContentBookkeepingSession.TryReserveQuotaAsync` is a conditional update that refuses when a new total would pass its limit, and a total equal to the limit fits. `TryCreateItemAsync` is a conditional create on tenant and content id. `RemoveStagedAsync`, `AddOwnerAsync`, `RemoveOwnerAsync` and `DeleteItemIfUnownedAsync` are conditional too, so two concurrent operations cannot both act on one entry (CWE-362, CWE-367).

**Isolation.** A backing isolates its transactions at read committed or stronger. The sweep decides what to delete from what the port reads, so an uncommitted removal must still name its bytes, and a rollback finds them there. `InMemoryContentBookkeeping` keeps a transaction's writes to itself until commit and holds a row lock on each staged or item entry it writes, so an overlapping writer waits. A pending reservation counts against concurrent ones, and a pending release frees nothing for anyone else until it commits, so a total never passes its limit.

**Byte order** (ADR 0113 decision 3):

1. An upload's bytes are complete and durable (`IContentByteWriter.CompleteAsync` has returned) before its staged entry is written.
2. Bytes and the wrapped key are destroyed only after the store's own transaction that removed their last entry commits. A delete that fails with an `IOException` or `UnauthorizedAccessException` does not fail the committed removal; the sweep finishes it.
3. Inside a caller's transaction the store cannot see that commit, so it leaves the bytes, and the sweep deletes them once no entry names them.
4. `content-staging-sweep` deletes bytes no staged or committed entry names once the grace period has passed, and never before.

**The grace period is `ContentStoreOptions.StagingExpiry`**, counted from when the bytes were last written: from the writer's completion, or from when it opened if it never completed. It adds no measurement of its own (Q19). A put writes its staged entry within its own put time, and ck-13 sets the expiry as a stated multiple of the longest put, so the sweep never takes bytes whose entry is still coming. A slower upload that does lose its bytes fails when its writer completes, and stages nothing.

**Known limit.** The sweep reads committed state. A caller that holds a joined put's staged entry uncommitted past the grace period could lose those bytes, so a caller must commit within the staging expiry.

Where the bytes live on the node, and the rule that no path deletes bytes during a backup, belong to the api backing ([ADR 0114](https://github.com/Harborline-Software/harborline-control/blob/main/adrs/ADR-0114-content-store-bytes-are-files-beside-the-node-database-and-each-key-stays-in-its-row/adr.md)). So does replaying a repeated idempotency key; the in-memory backing refuses one.

The internal test hooks that corrupt a segment or a wrap tag are not operations. Evidence is in `ContentStoreBoundaryTests.cs`, `ContentStorePortTests.cs` and `ContentStoreSaveStepTests.cs` in `hlp.foundation.content-store.tests`.
