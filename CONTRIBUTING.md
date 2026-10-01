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

## Maintainer validation flow

The standing delivery policy is Control's [PROC-0001 Change delivery](https://github.com/Harborline-Software/harborline-control/blob/main/procedures/PROC-0001-change-delivery/procedure.md).
This repository implements its Platform validation stages under T-577; it does not introduce a separate landing policy.

Keep a PR to one cohesive acceptance slice that can be reviewed and rolled back together. Use local
tests relevant to that slice while developing, then `node tooling/run-pr-preflight.mjs` before pushing (Node 24 and Git
Bash on Windows). Fetch `origin/main` first: recorded evidence provenance requires main's history.
The preflight checks the consumer-neutral boundary, prop vocabulary, recorded evidence provenance,
the existing catalog-preflight policy, and CI-contract fixture tests. It installs no dependencies,
performs no native or browser build, and writes no full-gate receipt. Catalog preflight uses the
gate's existing `--allow-stale-gate` mode; complete current-tree proof belongs to the landing gate.

Open a draft on the first remote push. Same-repository draft pushes get preliminary feedback;
`verify` still refuses draft admission with `BLOCKED`, which is a workflow guard, not a test failure.
Mark ready only when the acceptance slice and local verification are complete. Dependent PRs retain
the existing `stacked` label flow: their admission context is deferred. After the parent merges,
remove `stacked` first, then push the merge of `origin/main`; removing the label alone starts no run.
Do not use `stacked` to defer independent work. Superseding pushes cancel only the older PR run.

| Event | What `verify` proves |
| --- | --- |
| Same-repository, ready, unstacked PR: opened/synchronize/reopened/ready_for_review | Source-policy and CI-contract preflight; implementation/unclassified changes also require headless behavioral validation; browser landing proof awaits the queue |
| Draft | Preliminary feedback runs; admission is blocked |
| Stacked PR | Preliminary feedback runs; admission context retains its existing deferment |
| Fork PR | Admission is blocked; fork code is not run by this workflow |
| Merge group | Headless Phase-4 gate, all six browser shards, and collection must succeed |
| Manual `verify` dispatch | The same complete validation as a merge group on the selected ref |
| Push to main | Publication in `validate.yml`; no duplicate full gate |

The fast-only first slice covers contributor guidance and the CI-flow files explicitly listed in
`tooling/plan-pr-validation.mjs`, whose behavior the preflight fixtures exercise. Every other changed
path retains the existing headless gate on a ready, unstacked PR: native unit suites, tooling
self-tests, shared conformance, package-consumer checks and the other headless steps still provide
early behavioral feedback. Dependencies, build settings, source, fixtures, unknown paths and empty
or unreadable diffs cannot take the fast-only path. An unreadable plan retains headless validation;
a missing plan output fails admission. This conservative slice does not introduce affected-test
selection or remove behavioral PR feedback for ordinary implementation changes.

For gate or workflow changes, run full manual `verify` validation on the final branch ref and record
the run and exact SHA before considering it ready. A preliminary green check or a deferred check is
never full proof. Required `verify` and `sbom` contexts, security permissions and merge-queue rules
stay in force; failed, missing, cancelled or skipped full lanes fail `verify`. No partial result
receipt or prior-run reuse is introduced. The queue validates its cumulative merge-group SHA.

Other checks keep their existing policy: dependency review/SBOM and CodeQL retain their workflows;
Stryker.NET changed-project score floors and zero-mutant refusal are unchanged, and StrykerJS PR
feedback remains advisory with its existing refusal rules. Neither mutation workflow is a ruleset
required context, but this distinction does not waive a quality floor or a failed check.
Full/nightly mutation jobs remain separate from the landing gate.

Main publication is defined by `.github/workflows/validate.yml`. Release staging/publication is a
separate manual `.github/workflows/release-platform.yml` path with an approved main SHA and a
separately authorized publication input; successful validation does not authorize publishing.

GitHub's [workflow event documentation](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#merge_group)
describes why `merge_group` must report required checks independently from preliminary PR checks.

New module package identities require two real consumers, a deletion-test pass, an explicit aggregate facade, and a separate authority decision.

The earlier repository name has no npm or NuGet registry source (ticket 063); never add a registry fallback for that name or include contract-evidence archives in a published package.

Public registries may resolve third-party dependencies, but that external closure is not content-addressed by this repository.
