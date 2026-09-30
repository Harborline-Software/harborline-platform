# Platform R1 release path (T-705)

The owner separately authorizes publication. Dispatch `release-platform` from main with
the approved full main commit SHA that the dispatch itself runs at (GITHUB_SHA). `publish=false` is the default: it runs the complete
exact-tree gate and stages locally, without attestations, tags or release API writes.
`publish=true` signs the three payloads on GitHub-hosted Ubuntu, verifies repository,
workflow, source ref and commit, creates a complete draft, downloads and verifies all
assets again, then publishes the immutable release `v0.1.0`, titled R1. A failed run
never publishes; a failed draft remains for investigation. Reruns refuse an existing
tag. Do not delete or replace a draft without owner review.

The seed is the committed export checked by the existing exporter/conformance gate;
staging reads its bytes from the approved git commit, never a mutable workspace copy.
The existing strict receipt checker and a fresh full phase-4 receipt both gate staging.
All expired reviews block release, including reviews permitted to merge by the amnesty.
The workflow reuses the existing gate's dependency token; it adds no persistent access.
Immutable releases must remain enabled (owner D6). No API workflow or seed placement
is defined here. Library publication remains separate from the R1 asset lifecycle.

## Verify downloads

Install GitHub CLI and log in with any GitHub account; no team credentials or private
package feed is needed. Download the four assets from the platform `v0.1.0` release.
Obtain the approved full platform commit SHA from the release target independently of
any downloaded receipt. In the download directory:

```sh
sha256sum --check SHA256SUMS  # macOS: shasum -a 256 --check SHA256SUMS
RELEASE_COMMIT=<approved-full-platform-commit-sha>
for asset in platform-pack.export.json release-receipt.json SHA256SUMS; do
  gh attestation verify "$asset" --repo Harborline-Software/harborline-platform \
    --bundle provenance.sigstore.json \
    --signer-workflow Harborline-Software/harborline-platform/.github/workflows/release-platform.yml \
    --source-ref refs/heads/main --source-digest "$RELEASE_COMMIT" --deny-self-hosted-runners
done
```

Stop if any asset is missing or any command fails. Checksums alone do not establish
producer identity. Verification without a GitHub account remains T-716 and is not
claimed here. The API node installer and its platform seed consumption follow T-670.

M10's control-owned exit wording must say “GitHub build-provenance attestations
(Sigstore public-good) over every release artefact, verified by the published command”
in the implementing control change (owner D1); this platform PR cannot amend private
control. R1 does not claim OS code signing or a completed outsider node install.

The flags and bundle contract follow [GitHub CLI verification](https://cli.github.com/manual/gh_attestation_verify) and [actions/attest](https://github.com/actions/attest).
