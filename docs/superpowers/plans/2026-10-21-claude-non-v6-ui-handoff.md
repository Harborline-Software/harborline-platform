# Claude laptop handoff — non-v6, reporting-only first unit

Use this document as an assignment only when the Harborline lead (ROOT) provides it as the final reviewed handoff. The human starts the separate2019 Mac laptop session; this document never starts a task, Claude/cloud session, actor or coordinator. Earlier human handoff approval is retained; no repeat permission question is required.

The initial assignment is **Unit A reporting/reading only**: `REPORTING_ONLY_ALREADY_COVERED_NO_CONFIRMED_DEFECT`. There is no source writer, test/build/runtime, tool installation or publication grant. Preserve the existing source. ROOT adopted this initial disposition after reviewing the current ViewRuntime assessment; the later units below are conditional candidates, not queued coding assignments.

## Source and startup

Repository: [Harborline Platform](https://github.com/Harborline-Software/harborline-platform).

Canonical handoff URL: [this repository document](https://github.com/Harborline-Software/harborline-platform/blob/main/docs/superpowers/plans/2026-10-21-claude-non-v6-ui-handoff.md). ROOT must publish the reviewed revision through its normal documentation PR and provide the actual final link/revision before laptop work. A still-old published copy is not evidence that this replacement was delivered.

Authoritative **code baseline**: `ba8a3220233ffaf959e9b7a99fef32c3618aee2e`, tree `224bb657ede8d2ff81adadc8f52d4761b68a40c9`. The lead's newer handoff prose may arrive in a later documentation-only commit. Keep this reviewed handoff outside the checkout before selecting the exact code baseline, so selecting ba8 does not replace it with an older handoff. Use the complete normal GitHub repository; portable source excerpts are reference copies, not a new repository or build root. Verify the released source identity before work. Do not silently substitute a newer code revision or overwrite another checkout.

No board, shared inbox, Windows connection or Mac-specific private ledger is required to perform this reading/reporting assignment. Use repository-relative paths below. If authenticated repository access or exact source is unavailable, report that concrete prerequisite and stop instead of guessing.

Read `AGENTS.md` and `CONTRIBUTING.md`, then the selected catalog entry and Unit A inputs. Keep the current design and accepted working behavior. This is not a fresh campaign to redesign or re-audit every component, and it makes no all-components or four-day completion promise.

## Accepted delivery context and its limits

PR330 is merged at the code baseline. ROOT adopted the actual same-Mac fresh build/output audit: all32 normal child commands exited0. Its original outer capture FAIL remains preserved because a postcapture metadata check mishandled five legitimate relative SDK asset IDs; the independent data audit and ROOT adoption are separate records. No failed evidence was relabelled as a successful original run.

ROOT also adopted the current named reachable browser set at that source:20 Blazor observation records (19 positive snapshots plus one corrected phone intent) and18 React ordinary-form records (17 counter0 snapshots, then explicit submit1). These are recorded observations, **not38 automated tests or universal component certification**. Blazor covers representative desktop1600/physical700 embedding/phone390, owner redaction, dirty-close/local acknowledgment and standalone outside-focus cases. React covers actual packed tab/Keep/Pin/Unpin/Close/Cancel/Save/Discard controls inside an ordinary form, with a positive submit control.

The React serving audit joins exact HTML/CSS and the complete built executable JS prefix plus one LF and one inline source-map comment; it does not claim raw served JS is whole-byte equal. Normal ignored Storybook/Vite cache changes were recorded separately; no source/package/lock drift was hidden. Local save acknowledgments are fixture behavior, not backend persistence. Prior startup/origin failures and narrower native/browser limits remain retained. None of this certifies the2019 Intel laptop, future edits, queued stale callbacks in every browser, engine/Records/API, MAUI or a separate server.

The optional portable evidence packet contains the original build/browser adoption, independent audits, observations and screenshots. It is supporting provenance, not required private-ledger access or an execution grant.

## Unit A: ViewRuntime disposition before code

Authoritative inputs:

- `catalog/modules.yaml`: `hlp.ui.view-runtime`, owner `platform-ui`, interface7/quality1, React and Blazor projections, dependencies DataGrid/Button. Catalog status is metadata, not a fresh test verdict.
- `specs/modules/ui/hlp.ui.view-runtime/interface.yaml` and `quality.yaml`.
- `conformance/hlp.ui.view-runtime/fixtures.yaml`, `quality-fixtures.yaml` and `runners/run-shared.mjs`.
- `projections/react/ui/hlp.ui.view-runtime/src/ViewRuntime.tsx` and `src/__tests__/ViewRuntime.native.test.tsx`.
- `projections/blazor/ui/hlp.ui.view-runtime/HarborlineViewRuntime.razor` and `projections/blazor/ui/hlp.ui.view-runtime.tests/ViewRuntimeTests.cs`.

The historical report's missing-local-`:focus-visible` diagnostic does not establish a rendered ViewRuntime defect. This wrapper delegates keyboard/focus to DataGrid and action focus to canonical Button, which already define their focus behavior/styles. Both wrappers forward canonical row IDs and declared action IDs; existing source tests cover double-click row forwarding, action order/IDs, inert plans and disabled-action suppression. No confirmed implementation or rendered defect was found. Do not duplicate shared CSS, change adapters or alter a scanner merely to clear that diagnostic.

No exact keyboard/focus defect ticket was found in the selected unit documents. ROOT's Control reconciliation identifies T581 as a different broader layout scope and T820 as related plan-gate/row-activation history with an old mutant, not this exact defect. Do not invent a number, repurpose either ticket or promise to close it. The approved handoff, interface7 and quality1 supply the present reporting/triage basis.

Your useful first return is a short source-backed report:

1. Explain the existing row/action/focus delegation and where its source tests already cover the contract.
2. Distinguish callback/markup coverage from unproved browser-visible focus/traversal. Do not infer a fresh failure from a CSS string search.
3. Confirm the already-covered/reporting-mismatch disposition or record concrete contradictory evidence with exact source paths and stop for ROOT review.
4. State that source was preserved, no runtime was run and no ticket closure is claimed.

The existing neutral fixture supplies literal a1/a2 row IDs and declared action IDs. Any future authorized tests must invoke real production behavior and use literal/neutral/spec expectations independent of the code under test, per AGENTS.md; no production constant or validator output may be its own oracle.

### Prospective coverage subset — not a writer grant

If ROOT later decides missing wrapper coverage is worthwhile, its smallest proposed subset is exactly:

```text
projections/react/ui/hlp.ui.view-runtime/src/__tests__/ViewRuntime.native.test.tsx
projections/blazor/ui/hlp.ui.view-runtime.tests/ViewRuntimeTests.cs
```

Proposed cases would send Enter through real grid cells, expect literal a1/a2, keep row/action callbacks separate and prove actionsDisabled leaves row activation unchanged while actions remain inert. These additions are unwritten/unrun under this handoff. Native tests cannot establish visible CSS rings. Both adapters/types/authoring sources, shared dependencies and all project files remain read-only; a genuine failure requires a separate concrete repair proposal.

An optional browser-caller proposal is separate and limited to `gallery/projections/react/src/ViewRuntime.stories.tsx`, `gallery/projections/blazor/Stories/ViewRuntime.stories.razor` and `gallery/projections/blazor/Stories/ViewRuntimeScenario.razor`. No edits are granted. Any proposal must preserve canonical/generated scenarios and use existing literal row/action cases without new widgets/contracts or invented automated coverage.

## Later conditional units

| Unit | Current contract | Source-backed question, not an assumed defect |
|---|---|---|
| B1 Chart | `hlp.ui.chart`, interface1/quality1 | Reconcile old disabled/locale notes with existing visibility intent; invent no generic disabled API. |
| B2 Chat | `hlp.ui.chat`, interface1/quality1 | Check controlled composer/errors, callback order, keyboard focus, locale/RTL and narrow long-message reflow. |
| C Scheduler | `hlp.ui.scheduler`, interface1/quality1 | Check empty/optional collections, long labels and narrow reflow while retaining date/view/read-only/recurrence semantics. |

ROOT must separately reconcile requirements/tickets/active ownership and release an exact coherent subset before any later unit work. Preserve good existing behavior; report already covered when supported. One family per future commit, with distinct Spec/Standards review; unavailable reviewers mean review pending, never self-certification.

The historical family ceilings for a single released module M are `projections/react/ui/M/**`, `projections/blazor/ui/M/**`, `projections/blazor/ui/M.tests/**` and `conformance/M/**`. They are not current writer permissions. The literal sibling test trees are `hlp.ui.view-runtime.tests`, `hlp.ui.chart.tests`, `hlp.ui.chat.tests` and `hlp.ui.scheduler.tests` under `projections/blazor/ui/`; only the released family could apply. Existing gallery callers and optional `docs/usage/M.md` need explicit inclusion in a later release.

Generated module CSS/scenario copies, canonical interface/quality/style/scenario authority, catalog, shared RCL/test/project/export wiring, dependencies/locks and global tooling/CI remain read-only even inside a family ceiling. A required shared/authority change gets an exact separate proposal, not borrowed ownership.

## Explicit exclusions and active ownership

All v6/AppShell/Frame/TabHost/reducer/focus/scroll contracts, fixtures, tests, reusable frame and shared layout work are excluded. DataGrid and Button may be read to understand delegation; no repairs or certification of them are assigned. SchemaForm remains deferred. **Gantt is unapproved and unreserved; do not assign, reserve, launch or work on it.** No new module, widget, repository, package identity, NuGet publication or redesign is authorized.

**Windows Aggregates is an active exclusive source claim owned by the existing Windows team.** Do not edit its driver/tests/helper/fixtures, coordinate a duplicate dispatch or introduce a new actor/coordinator. All M11/T1035 callers, producers, feeds and Forms/Rules/Contracts/Records seams remain outside this laptop unit. Aggregates' engine-first timeline contract must not be detached or replaced by a synthetic timeline.

Held Records/J09/T616/e407/PR314, API9ab/Platformdaa1/App retry/T1100/private Q18/A5 bodies/operations stay excluded. Do not open held material to find a convenient ticket. No other-machine action, message, merge, push, deployment or automatic release is implied.

## Laptop setup and source-first integration

The2019 Intel laptop's exact compatible tools/SDK/feeds/artifacts and portability remain **UNVERIFIED**. Reporting/reading needs no product runtime. If repository access or legitimate pinned setup/artifacts are missing, record **SETUP_BLOCKED** with the exact missing prerequisite; a bounded source-only report can still be useful when source is readable. Do not install tools, copy arm64 binaries, unpin/upgrade the SDK, silently run full/global preparation, adjust mtimes or bypass freshness/cache checks.

Current source facts: global.json SDK11.0.100-rc.1.26425.128/rollForward disabled, pnpm11.1.3, installed Mac-mini Node metadata24.21.0, Storybook10.6.2 and Playwright1.62.1. These are not an Intel setup PASS or permission to run a recipe. ROOT must separately admit normal fresh target preparation before future coding/runtime.

For future source-first consumer work, preserve the complete Platform layout and .NET10+ library intent. Read `projections/blazor/ui/hlp.ui.app-shell/rev6/FRAME-USAGE.md` for the existing aggregate RCL ProjectReference using consumer-owned HarborlinePlatformSourceRoot. ViewRuntime specifically uses its existing style.css plus delegated data-grid.css; Chart/Chat/Scheduler have unique CSS, Chat/Scheduler import component JS automatically, and Scheduler uses existing AddHarborlineUiAdapters services. This does not assign v6 changes, a new host/project/server, MAUI or package publication.

Default full package-fixture/gallery preparation remains excluded. --packages-ready reuses packages and still installs/cleans/restores/builds; it cannot turn edited source into fresh package proof. No freshness/mtime/cache workaround is allowed. Missing pre-edit artifacts is a setup blocker, not permission to improvise.

## Local progress and return

Keep an append-only progress log outside the checkout: UTC, exact source commit, unit/requirement basis, paths read, disposition, limitations and next bounded step. No message board is required. A few checkpoints may cover source reconciliation, report review and return; they are not a four-day completion or availability/token/pricing promise.

Return one Markdown report with source commit/tree, concrete file/requirement references, existing coverage, remaining unproved behavior, final reporting-only disposition, source-preserved statement and blockers. No new test PASS, screenshot or ticket closure may be invented.

Any future separately released source delivery must include commits/patches, exact changed-path/SHA manifest, independent literal oracles, actual command/exit/log receipts, preserved failures and distinct review dispositions. Laptop source return is **BROWSER_PENDING**: ROOT/Mac mini owns fresh changed-source package/stamp/gallery and desktop/narrow rendered acceptance. Baseline20+18 observations never qualify future edits.

This document does not launch Claude, create a task, reserve Gantt, grant an Aggregates writer, install software or release source/runtime work. ROOT owns final document publication and the reporting-only release.
