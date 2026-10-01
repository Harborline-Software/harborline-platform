# Contributing

Harborline is not currently accepting outside contributions or pull requests.

The project will update this file if the policy changes after the first release and a separate decision to accept outside contributions.

For usage help or a bug report, follow [SUPPORT.md](SUPPORT.md).

## Maintainer work

Every canonical module has one interface, explicit projections, neutral conformance fixtures, and one artifact owner per packable projection.

Add source only with provenance and an executable clean-consumer gate.

Keep product workflows and cross-repository source links out of Platform.

The retained aggregate identities are local shadow candidates; never place them on a shared feed or mix them with earlier source artifacts.

Tests that mean it: Expected values come from an oracle independent of the code under test (AGENTS.md, Test oracles).

## Before you push

Fetch `origin/main`, then run `node tooling/run-pr-preflight.mjs` (Node 24; Git Bash on Windows). It checks the
consumer-neutral boundary, prop vocabulary, recorded evidence provenance, catalog policy and the CI-contract tests.
It builds nothing and writes no gate receipt. A pull request's `verify` result is preliminary as well: changes
outside the short CI-flow list in `tooling/plan-pr-validation.mjs` still run the headless gate on the pull request,
and complete proof is the merge-group run.

New module package identities require two real consumers, a deletion-test pass, an explicit aggregate facade, and a separate authority decision.

The earlier repository name has no npm or NuGet registry source (ticket 063); never add a registry fallback for that name or include contract-evidence archives in a published package.

Public registries may resolve third-party dependencies, but that external closure is not content-addressed by this repository.
