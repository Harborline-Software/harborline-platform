# Harborline.Foundation.ContentStore

The content store substrate ([DES-0054](https://github.com/Harborline-Software/harborline-control/blob/main/designs/DES-0054-content-store/design.md), T-521 slice 2): immutable bytes behind tenant-keyed `hmac-sha256:<hex>` content ids.

- **Put** streams bytes into a staged item, refusing at a named stage: `content.too-large`, `content.media-type-not-allowed`, `content.media-type-mismatch`, `content.digest-mismatch`. Size and quota refusals carry `limit` and `observed` in the DES-0014 C3 `detail`.
- **Commit** turns a staged item into a reference inside one critical section, counting record and tenant quotas in logical bytes and deduplicating within the tenant.
- **Read** serves an item only through a reference the gate has authorized, with `nosniff`, `Content-Disposition: attachment`, a strong ETag and single byte ranges.
- **Erasure** destroys the item's own data key, which the tenant key wraps. Each item is sealed as segmented AES-256-GCM after Tink's streaming AEAD.
- **The staging sweep** is registered with the execution runtime as `content-staging-sweep`.

The item limit, the quotas, the staging expiry and the segment size have no defaults. DES-0054 assigns them to the first DES-0037 measurement, so an installation must supply them.
