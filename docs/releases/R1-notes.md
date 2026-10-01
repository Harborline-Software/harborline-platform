# R1 platform

Platform publishes `platform-pack.export.json`, `release-receipt.json`, `SHA256SUMS`,
and `provenance.sigstore.json`. The bundle carries signatures over all three payloads;
it is verification material, not a recursively attested payload.

Sigstore provides integrity and provenance evidence; it makes no FIPS or CUI claim. CUI cryptography is documented separately in T-748–T-751.

This is the platform release. Node installation follows T-670 and the separate API release.
See [verification instructions](https://github.com/Harborline-Software/harborline-platform/blob/v0.1.0/docs/releases/platform-release.md). R1 uses GitHub build-provenance
attestations; operating-system signing belongs to T-715.
