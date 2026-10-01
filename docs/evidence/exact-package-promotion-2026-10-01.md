# Exact library consumption before publication — 2026-10-01

Scope: `.github/workflows/validate.yml`'s main-library publication path and `tooling/verify-package-fixtures.mjs --pack-libraries`.

The normal package consumer already used fresh scratch space and an isolated NuGet cache. The publication mode returned after packing and inspection, before that consumer. It now reaches the existing general consumer, its behavior markers, prohibition on source/project dependencies and exact Harborline package-closure assertion. The restored archives must then match the hashes obtained during inspection. Only this success writes the existing package manifest and a source/workflow/run/attempt-bound consumption receipt. Third-party offline feed inputs are removed from the publication inventory. Existing later specialized consumers on the normal gate path remain intact.

A verification command checks exact inventory, version, current archive hashes, manifest hash and receipt source/run identity before the existing T-705 attestation and push steps. Nothing repacks after consumption. The existing producer-derived package inventory stays authoritative.

## Evidence

- Five new guard tests and the existing publication inventory test pass. Cases include restored-cache substitution, missing/tampered staged files, wrong inventory, altered manifest/source/workflow/run/attempt and a copied bundle.
- The hash oracle is the literal SHA-256 test vector for ASCII `abc`; refusal cases mutate independently created fixture files. Consumer behavior assertions remain those of the existing shipped fixture. No mutation score is claimed.
- Actual `node tooling/verify-package-fixtures.mjs --pack-libraries` passed on the fresh `e679734c` worktree with 31 packages at `0.0.0-alpha.0.hf29b0279ef03`, running the existing general consumer and exact closure assertions. Post-pack validation checks all nuspec IDs, versions and SHA-256 values.

Manifest SHA-256: `7b0b9ac1b39f8233c4f686cbe3dd44bde049d2b602e44f98b96a53ffbe02edea`. Consumer-proof SHA-256: `1f0fc2c64dcf1658e51eaff78a898eff21af79230edc1c440acc365dd0701aea`.

## Limits

This is main-library package publication evidence, separate from the existing platform seed-release/receipt checks and T-670's published seed referent. Existing T-705 attestation permissions and steps are preserved. No registry push, release, tag, signing permission, credential or settings change was performed. The hash receipt itself is not a signature. Full specialized consumer coverage is not newly rerun in publication mode; its existing normal gate behavior is unchanged.

Existing `--skip-duplicate` behavior is preserved. This check binds the bytes supplied to the push command; it does not verify bytes already stored in a registry when a duplicate version is skipped.
