# HLP-0007 — The provenance record names only what this repository can check

Status: accepted (2026-09-05, ticket 269)

`docs/provenance/source-map.yaml` carried three claims per record: `contentHashes` (files in this
repository and their sha256), `sourcePaths` (the upstream files each projection was derived from),
and `sourceBlobs` (the git blob id of each of those paths at the source authority commit).
`validate-repository.mjs` checked only the first. The other two were never read by any tool or
document, so a reverted blob id, a renamed upstream path, and an invented entry were all equally
green.

They were also uncheckable. `sourceAuthority.commit` names a commit in the retired
pre-convergence authority repository; it is not reachable in `harborline-api` and cannot be
fetched. Measured against the api repository at both the pinned feed commit and its head, 2 of the
477 `sourceBlobs` entries matched the blob of their own path, and 252 of the 466 distinct
`sourcePaths` — including pre-063 project names — exist in no api commit at all. Re-pointing the
entries at a current commit would not have corrected the record; it would have replaced an unread
claim about one era with a fabricated claim about another.

So the two fields are dropped, and `retiredProvenanceFieldErrors` in `tooling/validator-policy.mjs`
fails the validator if either name returns on any record. `contentHashes` stays: it is a claim
about files this repository owns, and the validator verifies every one of them on every run. A
record keeps only the promises something checks.

If cross-repository derivation ever needs recording again, it is recorded against a commit the gate
resolves — the feed manifest's `sourceRepository` pin — and validated in the same commit that
introduces it.
