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
