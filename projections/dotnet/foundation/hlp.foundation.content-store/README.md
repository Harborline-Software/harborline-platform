# Harborline.Foundation.ContentStore

The content store substrate ([DES-0054](https://github.com/Harborline-Software/harborline-control/blob/main/designs/DES-0054-content-store/design.md), T-521 slice 2): immutable bytes behind tenant-keyed `hmac-sha256:<hex>` content ids.

- **Put** streams bytes into a staged item, refusing at a named stage: `content.too-large`, `content.media-type-not-allowed`, `content.media-type-mismatch`, `content.digest-mismatch`. Size and quota refusals carry `limit` and `observed` in the DES-0014 C3 `detail`.
- **Commit** turns a staged item into a reference inside one critical section, counting record and tenant quotas in logical bytes and deduplicating within the tenant.
- **Read** authorizes before range evaluation, then returns a segment-streaming body. A first-segment or data-key failure is a typed `ContentIntegrityException` before a host writes headers; a later failure escapes the body so the host aborts it. Delivery uses `filename*` (RFC 8187 UTF-8) after an ASCII `filename` fallback where needed.
- **Erasure** destroys the item's own data key. Each item is sealed in fixed 65,536-byte AES-256-GCM segments; the item records that size. HKDF-SHA-256 derives separate tenant subkeys for content ids and data-key wrapping.
- **Ports.** `IContentStoreStorage` owns item metadata, ciphertext segments, wrapped keys, staged items, reclamation entries and integrity episodes. `InMemoryContentStoreStorage` is one implementation; a host supplies durable storage. `IContentStoreQuotaBase` declares the host-owned Q22 volume measurement.
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
