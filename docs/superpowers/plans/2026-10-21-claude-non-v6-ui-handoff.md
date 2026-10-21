# Claude laptop handoff: non-v6 Platform UI

**DRAFT — not released for execution.** Prepared 2026-10-21 for the separate 2019 Mac laptop.
The user will start this work after the current v6 delivery is finished. This document does not start a session or move existing ownership.

## Release and source identity

| Field | Frozen value / release requirement |
|---|---|
| Repository | `https://github.com/Harborline-Software/harborline-platform` |
| Reference source commit | `cec76704e322a9edbb937db6524276f0d581c41f` |
| Reference source tree | `0c758dd9f17d6c7dff191070033aa4f4ea2abcb2` |
| Document branch | `codex/claude-non-v6-ui-handoff-20261021` |
| Execution release | **PENDING:** lead records the post-v6 source commit/tree, reviewed document commit, active ownership exclusions and released units here before launch. |
| Laptop work branch | Create `claude/non-v6-ui-20261021` from that released source commit. |
| Assigned role | Claude authors the released non-v6 UI units; Mac mini lead integrates and validates returned work. |

The source reference above precedes in-flight v6 and M11 work; it is not an instruction to overwrite or cherry-pick those branches.
The document's own commit cannot be its embedded source pin. The lead supplies its exact document commit in the launch message and freezes the separate execution base here.
Keep a copy of this document outside the checkout before switching branches; that preserves the instructions when the execution base lacks this file.
If the execution release is still PENDING, finish preparation and report the missing release; make no component edits.

## Mission and completion boundary

Preserve the existing visual design and good behavior. Complete concrete unfinished non-v6 component behavior and meaningful React/Blazor evidence, one module family per commit.
Begin with the catalog-led triage below; existing acceptance is retained within its recorded scope. An old gap is a candidate to confirm, not proof that today's implementation is broken.
Return source commits, independent review results and actual browser evidence to the Mac mini lead. Work can run independently: **no message board, shared inbox, Windows chat or remote machine access is required.**

Delivery stays in this repository's existing projects. Blazor consumers pull source and add a `ProjectReference` to the existing Razor Class Library, with its styles, required JavaScript and a minimal usage/setup example.
Use host-supplied content and callbacks; keep backend and demo data separate. Retain the reusable .NET10+ design intent.
Acceptance is actual component behavior in the existing Blazor gallery/Storybook and, where parity applies, React Storybook: real CSS/JS, desktop and narrow-screen interactions.
NuGet publication, a new repository, MAUI certification and a separate server-host certification are not delivery requirements. Local packages used by the existing gallery are test plumbing.
This assignment does not assert that all components can be finished in four days.

## First preparation: finish before implementation

1. Read root `AGENTS.md`, `CONTRIBUTING.md`, and any nearer instructions for the selected module. Preserve independent test oracles and normal hooks/review.
2. Verify the released source commit/tree and a clean checkout. Record Git identity and installed tool versions in the local progress log.
3. Read the selected module's catalog entry, exact interface and quality revisions, conformance fixtures, existing adapters and gallery stories. Record actual ticket references found there; an exact ticket is **not yet bound by this draft**. Do not invent an issue number.
4. Confirm that the released unit's entire writable mask is free of another owner. The frozen release must explicitly resolve the exclusions below. If a shared-file or contract change is needed, stop that unit and return the exact proposed delta.
5. Reproduce the named gap against that source, or record that it is already covered/only a reporting-model gap. Specify literal expected behavior from the existing spec or an independent fixture before editing.
6. Record `STARTED` with UTC time, module, base/tree, path mask, intended acceptance cases and process ownership. Only then edit the released unit.

Done means all six items are recorded, the gap is concrete and the scope is unchanged. Missing tools, source pins, ownership or contract authority produce a useful BLOCKED return, not a guessed workaround.

## Catalog triage: bounded evaluation of the rest

The [module catalog](../../../catalog/modules.yaml) declares 81 UI modules at the reference base; [projection declarations](../../../catalog/projections.yaml) include differing adapter coverage.
The retained [gate-model report](../../evidence/gate-model/ui-gate-model.json) has 80 rows, 15 terminal; its top-level PASS is not an all-component PASS. The catalog's preview/nonvisual Records-authoring row is absent from that report.
These are retained counts, not measurements from this laptop. The report lacks a current-run source/date binding.

Create one local matrix from existing catalog/report metadata: module ID, declared adapters, existing accepted scope, known gap, current source reference, next action and confidence.
Exclude v6 and held families before selecting work. Preserve accepted unchanged components; do not repeat every visual review or treat 50 expired approvals in frozen backlog 334 as a new approval campaign.
Distinguish missing implementation, missing fixture/browser evidence, scanner vocabulary mismatch and unknown status. The retained 62 unbuilt client-performance profiles are an evidence backlog, not 62 reproduced regressions.
For modules outside the released units below, return a prioritized candidate list with exact source/spec paths and proposed masks. This draft grants evaluation, not unrestricted edits to every catalog entry.

## Frozen candidate units: release individually

Each unit is **conditional**, pending the release block and current-source/ticket reconciliation. Use the priority order; do not run overlapping writers.
All listed module IDs have catalog owner `platform-ui`. Catalog status “conformant” describes an adapter declaration, not fresh runtime proof.

| Unit | Authoritative contract and retained gap | Required result after confirmation |
|---|---|---|
| A — View Runtime | `specs/modules/ui/hlp.ui.view-runtime/interface.yaml` rev 7; quality rev 1; report row identified by `moduleId: hlp.ui.view-runtime`. Retained direct FAIL: missing `:focus-visible`. | Confirm the actual focus route in both adapters; correct only the missing behavior. Prove visible keyboard focus, traversal and activation in rendered stories; preserve pointer behavior, callbacks and existing appearance. |
| B1 — Chart | `specs/modules/ui/hlp.ui.chart/interface.yaml` rev 1; quality rev 1. Retained missing disabled-state scenario and locale fixtures. | Determine whether “disabled” is an actual public state or scanner mismatch from the exact interface. Add meaningful cases or a bounded implementation repair only when required; prove accessible data, locale formatting and adapter parity. |
| B2 — Chat | `specs/modules/ui/hlp.ui.chat/interface.yaml` rev 1; quality rev 1. Retained error-state and reflow gaps; locale fixtures absent. | Exercise the existing error contract, long messages, focus/callback order, locale/RTL and narrow reflow. Repair confirmed behavior while preserving controlled ownership and established design. |
| C — Scheduler | `specs/modules/ui/hlp.ui.scheduler/interface.yaml` rev 1; quality rev 1. Retained empty-collection and reflow gaps. | Exercise empty events/data and relevant optional collections, long labels and narrow width; preserve recurrence/date/view semantics and read-only behavior. Change only a confirmed requirement. |
| D — Schema Form, deferred | `specs/modules/ui/hlp.ui.schema-form/interface.yaml` rev 4; quality rev 1. Retained loading/locale and Blazor performance evidence gaps. | Evaluate metadata only until lead explicitly releases it after DynamicForms integration reconciliation. No change to Rules, Forms contracts, field semantics or shared fixtures under this draft. |

For each unit, authoritative neutral inputs are `conformance/<module-id>/fixtures.yaml` and `quality-fixtures.yaml`; its runner is `conformance/<module-id>/runners/run-shared.mjs`.
Read the exact referenced bodies before choosing assertions. This handoff verified catalog references and path existence, not their complete implementation or current browser outcome.
Keep each unit separate: B1 and B2 are separate commits and separate acceptance results. If a candidate is already fixed, report the source and observed evidence and move to the next released unit.

## Exact write boundaries

For a released module ID `M` from A/B1/B2/C only, these are the complete family masks:

- `projections/react/ui/M/**` and `projections/blazor/ui/M/**`.
- `conformance/M/**`, preserving existing fixture identities and independent literal oracles.
- `gallery/scenarios/M.json`.
- The corresponding React story and Blazor story/scenario files in the table below.
- A unit-specific usage note under `docs/usage/M.md`, only if needed to explain actual source-first setup; no new package identity.

| M | Exact gallery files |
|---|---|
| `hlp.ui.view-runtime` | `gallery/projections/react/src/ViewRuntime.stories.tsx`; `gallery/projections/blazor/Stories/ViewRuntime.stories.razor`; `gallery/projections/blazor/Stories/ViewRuntimeScenario.razor` |
| `hlp.ui.chart` | `gallery/projections/react/src/Chart.stories.tsx`; `gallery/projections/blazor/Stories/Chart.stories.razor`; `gallery/projections/blazor/Stories/ChartScenario.razor` |
| `hlp.ui.chat` | `gallery/projections/react/src/Chat.stories.tsx`; `gallery/projections/blazor/Stories/Chat.stories.razor`; `gallery/projections/blazor/Stories/ChatScenario.razor` |
| `hlp.ui.scheduler` | `gallery/projections/react/src/Scheduler.stories.tsx`; `gallery/projections/blazor/Stories/Scheduler.stories.razor`; `gallery/projections/blazor/Stories/SchedulerScenario.razor` |

`M` is one literal module ID, never a glob for all UI. Before edits, enumerate existing files inside that mask and record the intended subset. Creating a missing test inside that same family is allowed after release.
Specs, interface/quality revisions, authoritative `specs/.../style.css` and generated baselines remain read-only. If the fix requires their change, return a separate proposal and stop that unit; do not modify generated/approved evidence to turn a failure green.
Module-local adapter CSS and JS stay with their module and preserve established visual tokens. Any shared style/export/build entry-point change needs a separately reviewed scope.

## Other owners and excluded seams

**All v6 work is excluded**, including its components, demo frame, module, fixtures, shared reducer, focus contract, specs, conformance and gallery scenarios. Do not transfer, repair or certify it here.
Also excluded: app-shell/app-layout/layout-runtime and shared shell/scroll/tab-host infrastructure; root catalog, global dependencies/locks, project files, aggregate export surfaces, CI and `tooling/**` edits.
The existing RCL is `projections/blazor/ui/hlp.ui.button/Harborline.UIAdapters.Blazor.csproj`; its shared project is read-only even when it includes the selected module. React aggregate/package wiring is read-only too.

Windows retains the actual DynamicForms five-path unit:

- `tooling/verify-package-fixtures.mjs`
- `tests/package-consumers/dynamic-forms-npm/package.json`
- `tooling/tests/package-fixture-frozen-install.test.mjs`
- `tests/package-consumers/dynamic-forms-npm/pnpm-lock.yaml`
- `tests/package-consumers/dynamic-forms-npm/pnpm-workspace.yaml`

Do not edit these, the primary consumer/helper, producer/feed provenance, or shared Forms/Rules/Contracts/Records primitives. No additional Windows primitive assignment is established by this handoff.
Private Q18/A5 evidence and held Records/J09/T616/e407/PR314, API9ab, Platformdaa1, unknown App state and T1100 operations remain out of scope: do not read their private bodies, retry their runtime, substitute another executor or infer release from this UI assignment.
If a chosen component depends on one of these seams, preserve the baseline, state the exact dependency and continue only another already released independent unit.

## Toolchain and source-verified recipes

This section is a recipe from the reference source, **not a clean-laptop setup PASS**. No install, build, browser or product execution was performed while authoring it.
Check 2019 Mac OS/architecture support and installed tools before starting; stop with exact missing prerequisites rather than upgrading pins or adding feeds.

| Tool / project | Verified source fact |
|---|---|
| Git and Node | Git is needed for isolated commits. `CONTRIBUTING.md` requires Node 24 for preflight. |
| Package manager | Root and gallery manifests pin `pnpm@11.1.3`; use repository lockfiles and frozen installs. |
| .NET SDK | `global.json`: `11.0.100-rc.1.26425.128`, `rollForward: disable`. |
| Blazor browser gallery | `gallery/projections/blazor/Harborline.Gallery.Blazor.csproj` targets `net11.0`; this does not authorize retargeting the library or changing .NET10+ design intent. |
| React gallery | `gallery/projections/react/package.json`: Storybook `10.5.10`, `dev`, `build` and `typecheck` scripts. |
| Browser tools | `gallery/tests/package.json`: Playwright `1.62.1`, axe `4.12.1`; `test`, `typecheck`, `install:browser`. |

From repository root, record versions and source first:

```sh
git rev-parse HEAD
git rev-parse 'HEAD^{tree}'
git status --short
node --version
pnpm --version
dotnet --version
```

Read `gallery/README.md` and `tooling/prepare-galleries.mjs` before the first gallery preparation. The existing galleries consume local packed artifacts and repository-isolated dependencies; a bare clean checkout does not contain the Mac mini's prepared feed.
Use the current repository's documented preparation; retain logs and exact source identities. Missing packages/feeds are a prerequisite failure. Keep normal freshness checks; do not replace the gallery with source imports or set `HarborlineSkipFeedFreshnessCheck`.
The previously accepted Mac browser005 route reused seven already-built local packages on a different v6 candidate; its result does not establish this laptop's setup or these non-v6 scenarios.

The default `gallery:prepare`, `gallery:react`, `gallery:blazor` and unqualified gallery-gate commands run the full package-fixture driver and cross-repository feed preparation before browser selection. They are unsuitable under this handoff's exclusions. `GALLERY_GREP` restricts browser tests only; it does not restrict preparation.

Before execution, the lead must supply an allowed, source-bound prepared-artifact set and release the exact preparation scope. Verify its archive provenance and all normal source/feed freshness checks. If those prerequisites are absent, return **BLOCKED: prepared gallery artifacts unavailable**; do not manufacture readiness or run the full fixture driver.
The supported `--packages-ready` route reuses valid artifacts but still installs gallery dependencies, cleans gallery caches, restores and builds. It is not a held-operation waiver. Review those remaining operations against the release before running it; keep the prohibition on freshness bypasses.

Only after that prerequisite and scope review, the existing driver supports focused browser tests through `GALLERY_GREP` and screenshots through `--capture`:

```sh
# CASE_PATTERN must first be matched to actual test titles for the selected unit.
# RUN_DIR is an absolute, unique laptop-local evidence directory created by you.
GALLERY_GREP="$CASE_PATTERN" node tooling/run-gallery-gate.mjs --packages-ready --capture \
  > "$RUN_DIR/gallery-result.json" 2> "$RUN_DIR/gallery.log"
```

Before choosing `CASE_PATTERN`, read the selected scenarios and `gallery/tests/tests/gallery.spec.ts`; list the matching tests with the existing Playwright test script, and record their exact names/count. A zero-test run fails acceptance.
The driver starts React on loopback 6106 and Blazor on 6107, builds the existing galleries, installs/checks Chromium and runs the focused browser selection. A successful focused report is `CHECKED`, not full-library `PASS`.
Use manual gallery interaction alongside automated assertions when a named requirement lacks an existing scenario. Keep initial failures, final logs, actual screenshot dimensions and browser console findings; report warnings honestly.
Source-verified per-gallery scripts are `pnpm --dir gallery/projections/react run typecheck` / `run build`, and `pnpm --dir gallery/tests run typecheck` / `run test`; use `pnpm --dir gallery/tests exec playwright test --list` to enumerate titles. Read selected test/runner configuration before invoking native/conformance commands; no guessed all-project command is required here.

## Per-unit acceptance and review

- Exercise the actual rendered Blazor component with its packaged/source-first CSS and needed JS; also exercise React for an existing parity contract. Verify desktop and a recorded narrow viewport, with keyboard and pointer behavior relevant to the unit.
- Pin expected semantics to the existing spec/neutral fixture or literal independent oracle. A CSS text search, model round-trip or adapter declaration alone is insufficient.
- Preserve initial failing evidence, show the smallest correction and rerun that exact case plus connected regression cases. A reporting-only mismatch gets a truthful finding, not invented component behavior.
- Document the actual `ProjectReference`, public namespace/setup and asset URLs/imports used by the selected example after reading the current module/library instructions. Verify the example in the existing browser gallery. No new package or registry identity is implied.
- Obtain independent Spec/Standards and adversarial review of the complete final unit where the available Claude environment supports reviewers. Record reviewer identities, source commit and findings; if unavailable, label review pending for Mac mini rather than self-certifying independence.
- Run the normal repository pre-push requirement after fetching current main: `node tooling/run-pr-preflight.mjs`. Do not bypass hooks or protected merge. If excluded shared changes arrived upstream, return the conflict rather than silently widening the unit.

Done means the whole unit delta is inside its mask, independent reviews are resolved or honestly pending, named browser cases ran, setup/asset instructions match the exercised component, and the return packet states remaining limits. No automerge or publication.

## Local progress and return protocol

Keep an append-only log outside the checkout, for example `~/harborline-claude-return/PROGRESS.md`; put screenshots and original logs under a unique per-unit child. This path is laptop-local and has no shared-drive requirement.
Each entry records UTC time; state (`PREPARED`, `STARTED`, `BLOCKED`, `REVIEW`, `RETURNED`); base/tree; module and writable paths; commands; exit/result; evidence filenames; next action.
Commit one module family at a time on the laptop work branch. Keep evidence outside packages; return a compact manifest with paths, sizes and SHA-256 values. Never record tokens or credentials.

Return a self-contained summary to the user for the Mac mini lead:

```text
Repository / branch / execution base and tree / final commit(s):
Unit and literal requirement; source of expected behavior:
Files changed; confirmation of excluded-file preservation:
Initial reproduction and exact final browser/test names/counts/results:
Desktop/narrow viewport; CSS/JS/example actually exercised:
Independent reviewers and unresolved findings:
Original logs, screenshots and SHA-256 manifest:
Setup prerequisites or blocked dependencies; remaining component gaps:
Exact commits to integrate; no merge performed:
```

A Thursday–Sunday sequence is optional: Thursday bind tools/source and confirm Unit A; Friday complete and review one family; Saturday integrate accepted returns and take the next released family; Sunday repair observed regressions and return residual gaps.
These are checkpoints, not a promise of capacity or completion. No Claude model availability, token allowance, pricing, software installation or remote-machine action is assumed.
