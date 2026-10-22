# Claude laptop handoff — evaluate and repair non-v6 UI

When the lead provides this reviewed assignment and the human starts the separate 2019 Mac laptop session, **evaluate, then implement demonstrated requirement-backed repairs within the exact paths below**. Earlier human approval remains valid. Ordinary source tests, project-local setup, source RCL compilation and private commits are included; no further generic permission or old defect ticket is required. This document does not launch a session/task/coordinator.

Triage ViewRuntime, then repair Scheduler C, Chart B1 and Chat B2. Preserve design; record blocked lanes and continue others. Return coherent slices/residual gaps **BROWSER_PENDING**; lead owns fresh packages/rendered acceptance. No all-components promise.

## Repository and exact startup

Repository: [Harborline Platform](https://github.com/Harborline-Software/harborline-platform). [Canonical handoff document](https://github.com/Harborline-Software/harborline-platform/blob/main/docs/superpowers/plans/2026-10-21-claude-non-v6-ui-handoff.md).

Code baseline: **`ba8a3220233ffaf959e9b7a99fef32c3618aee2e`**, tree **`224bb657ede8d2ff81adadc8f52d4761b68a40c9`**. Record the later documentation revision separately. Save this reviewed document outside the checkout before switching to ba8, which contains older prose. Use the complete repository. No board/inbox/private ledger is required.

Read `AGENTS.md` and `CONTRIBUTING.md`; verify a clean checkout. Preserve existing branches/changes without force/reset. On a new clean checkout:

```sh
git switch -c claude/non-v6-ui-20261022 ba8a3220233ffaf959e9b7a99fef32c3618aee2e
git rev-parse HEAD 'HEAD^{tree}'
git status --short
```

Resume an existing branch without replacement; report missing source/access, never substitute main. Keep evidence outside checkout. One session/writer, one family/commit. Serialize installs/shared RCL/.NET work using common node_modules/bin/obj.

## Requirements and writer boundary

For M=`hlp.ui.view-runtime`, `hlp.ui.scheduler`, `hlp.ui.chart`, `hlp.ui.chat`, read `catalog/modules.yaml`, `specs/modules/ui/M/{interface,quality}.yaml` and `conformance/M/{fixtures.yaml,quality-fixtures.yaml,runners/run-shared.mjs}`. ViewRuntime is interface7/quality1; others interface1/quality1. These contracts/neutral cases are the requirement basis. Catalog status/old warnings are not fresh verdicts. Cite cases; invent no ticket or broader closure.

Only these existing files may be changed:

| Slice | Exact repository-relative writable paths |
|---|---|
| Scheduler C1/C2 | `projections/react/ui/hlp.ui.scheduler/src/Scheduler.tsx`; `projections/react/ui/hlp.ui.scheduler/src/__tests__/Scheduler.conformance.test.tsx`; `projections/blazor/ui/hlp.ui.scheduler/HarborlineScheduler.razor`; `projections/blazor/ui/hlp.ui.scheduler.tests/SchedulerTests.cs` |
| Chart B1 locale | `projections/blazor/ui/hlp.ui.chart/HarborlineChart.razor`; `projections/blazor/ui/hlp.ui.chart.tests/ChartTests.cs` |
| Chat B2 keyboard/catalog/semantic cases | `projections/blazor/ui/hlp.ui.chat/HarborlineChat.razor`; `projections/blazor/ui/hlp.ui.chat.tests/ChatTests.cs`; only if selective keyboard cancellation needs it, existing `projections/blazor/ui/hlp.ui.chat/wwwroot/chat.js` |

Exact files only; ViewRuntime is read-only. Other tests/gallery callers/types/exports/shared modules/catalog/interface/quality/style/scenario authority/neutral fixtures/generated copies/projects/locks/workspaces/tooling/CI remain read-only. Ignored local dependency/compiler outputs are permitted. A needed shared/API/extra-path change becomes a precise return proposal; continue other slices. Later catalog triage gives no extra writers.

Exclude all v6/AppShell/Frame/TabHost/reducer/focus/scroll, DataGrid/Button repairs, SchemaForm, M11/T1035/Forms/Rules/Contracts qualification and Records/API/private holds. **Gantt is unapproved/unreserved; Windows Aggregates has an active exclusive Windows source claim.** No duplicate dispatch, driver/helper/fixture changes or engine/timeline detachment. Do not open held Records/J09/T616/e407/PR314, API9ab/Platformdaa1/App retry/T1100/private Q18/A5 bodies. Reading admitted interfaces/compiling dependencies grants no ownership or unrelated test execution.

## First: brief ViewRuntime A disposition

Read wrappers/native tests:

- `projections/react/ui/hlp.ui.view-runtime/src/ViewRuntime.tsx` and `src/__tests__/ViewRuntime.native.test.tsx`.
- `projections/blazor/ui/hlp.ui.view-runtime/HarborlineViewRuntime.razor` and `projections/blazor/ui/hlp.ui.view-runtime.tests/ViewRuntimeTests.cs`.

Missing local `:focus-visible` is not a rendered defect: focus delegates to DataGrid/Button; literal row/action forwarding has existing coverage. Record **already covered / no confirmed local defect**, or contrary evidence, distinguishing callbacks from visible focus. No A writer; optional source tests are allowed. Do not duplicate CSS/change a scanner or stall later units. T581/T820 are not an exact current focus ticket.

## Scheduler C: reproduce two mechanisms, repair four files

Read adapters/tests/revision1. Source mechanisms below have **UNRUN runtime reproduction**.

**C1:** Blazor's narrow fallback chooses Agenda without checking EffectiveViews. `Views=[Day], DefaultView=Day, Narrow=true` validates but selects an excluded view. React already checks `viewTypes.includes('agenda')`. Repair the private fallback membership only; preserve controlled View, an explicit user selection and breakpoint semantics.

**C2:** React completion handlers lack readOnly guards and render stored editor/recurrence state after `readOnly=true`. Blazor also retains those dialogs, although its completion handlers already refuse mutation. On replacement with readonly, both must cancel pending edit/recurrence state, remove mutation UI and avoid resurrection when re-enabled. No new parameter/event or recurrence redesign.

Use real component rendering and literal dates/events: `2026-08-11`, event `a`/`Inspection` 09:00–10:00, recurring `r` with `FREQ=DAILY;COUNT=2`. Required independent oracles:

1. Narrow Day-only: Day/one day column, no Agenda, zero unsolicited ViewChanged. Day+Agenda still defaults Agenda; explicit Day persists; controlled Day wins. Use existing matchMedia or Narrow/fake observer; preserve breakpoint.
2. Open create/edit/recurrence-edit/delete, then replace readOnly through real props. Remove mutation dialogs/controls; add/update/delete counts exactly0. Preserve date/view/event/master/exception bytes; cover occurrence/series. Re-enable without stale resurrection/callbacks; a fresh action emits one expected neutral request. Retain a safe focus target.
3. Check actual Day/Week/Month/Agenda with empty data/readonly at mount; retain normal editing/date/Today/recurrence/anchor/locale regressions. defaultView is initial-only: use controlled view or fresh mounts and assert view before interaction.

Localized empty Agenda/responsive CSS already exist. Residual gesture evaluation: Blazor double-click hour spans have pointer-events:none in authority CSS and no event drag/drop handlers. Return reproduction/proposal, not a gesture redesign or full parity claim.

## Chart B1: catalog text must win over a fallback

Read Chart adapters/tests/read-only locale interfaces. Blazor `Translate` passes English `instanceOverride: fallback` to `ResolveString`; `HarborlineLocaleScope` prioritizes it over catalog chart/series/item/value text. React honors catalogs. Runtime reproduction is UNRUN.

Repair only the Chart-local helper and its existing bUnit tests: resolve catalog/default text first, using the fallback only for a genuinely unresolved key. Preserve explicit public AccessibleName, Title, DataSummaryLabel and Empty precedence and the shared locale provider contract. Required literal cases:

- Host catalog `charts.chart=Graphique`, `charts.series=Série`, `charts.item=Élément`, `charts.value=Mesure`: Line Jan/Feb, series A `[12,null]`, default figure name `Graphique`, first heading `Série`, summary `Graphique: Mesure`; caller order and missing null retained.
- Donut Palm10/Bay20: headings `Élément`, `Mesure`; ordered Palm/Bay rows and a test-owned formatter yielding `amount:10`, `amount:20`.
- Existing Arabic catalog `مخطط / السلسلة / العنصر / القيمة`, rtl and preserved caller order. A catalog replacement updates generated text without mutating input data.
- Explicit accessible name `Revenue trend` wins; Title wins when name is absent; explicit summary `My ordered data` and Empty `Nothing measured yet` remain intact.

Preserve React locale parity. No generic disabled API. Bounds/singleton-donut/fixture-coverage leads remain residual; no full Chart completion claim.

## Chat B2: selective keys, existing catalog, useful fixtures

Read adapters/local JS/tests/revision1/22 neutral cases. Blazor cancels all article keydown defaults while handling only adjacent arrows, cancelling Tab/Shift+Tab too; React cancels handled arrows. Blazor builds English names instead of `ai.chat.messageArticle`/`ai.chat.messageArticleTime`. Its 22-row theory repeats one empty-log assertion, not 22 semantic proofs. Reproduce these source findings.

Make cancellation selective, using existing local JS only if needed. Add targeted neutral-case semantics without rewriting all 22/shared machinery. Use existing templates, preserving timestamp instant/active locale without a new cross-runtime date pattern:

- For literal m1/m2/m3, m2 Up→m1 and Down→m2, without wrapping at either edge. Ordinary Tab/Shift+Tab and unrelated defaults must remain usable; source tests must exercise the real local decision/event route. A bUnit handler invocation alone cannot prove browser Tab traversal; name that remaining browser case.
- Override roleUser=`Test user`, roleAssistant=`Test assistant`, messageArticle=`Article [{role}]`; assert `Article [Test user]`, `Article [Test assistant]`, `Article [Automation]`. Timed templates use the existing role/time placeholders and active locale.
- Controlled `"  inspect  "` yields ordered submit(`inspect`), then change(`""`); whitespace yields neither. Evaluate click, Enter and suggestions distinctly; disabled routes neither submit nor clear. Preserve ordered roles, replacement removing stale IDs, assistant/system-only suggestions versus first-user suppression, streaming/typing semantics and slots.

Within these same source/test files, optionally test explicit Placeholder versus provider precedence before repair. No React production/transport/retry/AI/error-contract rewrite. Long text/200% zoom/RTL/error callers are browser residuals, not gallery writers.

## Legitimate laptop setup and ordinary source commands

2019 Intel portability is **UNVERIFIED**. Use legitimate installed tools: global.json SDK `11.0.100-rc.1.26425.128`/rollForward disabled, Node24/pnpm11.1.3; libraries target net10, tests net11. No global tool/SDK install, arm64 copy, unpin/upgrade, new first-party registry fallback, cache cleanup, mtime/freshness bypass.

Project-local frozen install/NuGet restore are allowed. In actual workspace `projections/react/ui/hlp.ui.button`, `CI=1 pnpm install --frozen-lockfile` must preserve locks/workspace/patches/pins. If approval/config changes are required or tracked setup files change, retain the failure/diff; do not approve scripts or force configuration. Report SETUP_BLOCKED and continue available units. Normal restore/build may write ignored bin/obj and restore central third-party pins; no new first-party feeds/projects.

From repository root, the ordinary source suites are:

```sh
pnpm --dir projections/react/ui/hlp.ui.button run test:scheduler
pnpm --dir projections/react/ui/hlp.ui.button run test:chart
pnpm --dir projections/react/ui/hlp.ui.button run test:chat
pnpm --dir projections/react/ui/hlp.ui.button run test:view-runtime
```

These use module source aliases, **no gallery feed/stamp**. Run serially; no test:native/run-native/full fixture driver. They do not certify rendered CSS/focus.

The shared bUnit project includes sibling tests and directly references source RCL/dependencies. Use installed pinned dotnet. The recipe below is Scheduler; Chart's filter is `FullyQualifiedName~Harborline.UIAdapters.Blazor.Tests.ChartTests|FullyQualifiedName~Harborline.UIAdapters.Blazor.Tests.ChartPerformanceTests`; Chat's is `FullyQualifiedName~Harborline.UIAdapters.Blazor.Tests.ChatTests|FullyQualifiedName~Harborline.UIAdapters.Blazor.Tests.ChatPerformanceTests`:

```sh
dotnet test projections/blazor/ui/hlp.ui.button.tests/Harborline.UIAdapters.Blazor.Tests.csproj --configuration Release --filter 'FullyQualifiedName~Harborline.UIAdapters.Blazor.Tests.SchedulerTests|FullyQualifiedName~Harborline.UIAdapters.Blazor.Tests.SchedulerPerformanceTests' --logger 'trx;LogFileName=scheduler.trx' --results-directory <ABSOLUTE_UNIQUE_EVIDENCE_DIR> -nodeReuse:false -maxcpucount:6 -p:UseSharedCompilation=false
```

Keep names/counts in fresh evidence directories. No no-build/no-restore without prerequisites. Source RCL build is permitted:

```sh
dotnet build projections/blazor/ui/hlp.ui.button/Harborline.UIAdapters.Blazor.csproj --configuration Release -nodeReuse:false -maxcpucount:6 -p:UseSharedCompilation=false
```

Recipes are source-verified, **not laptop PASS**. Missing .NET does not block React; missing gallery artifacts blocks browser only. No broad producer build/full prepare for focused tests.

## Source-first integration and browser handback

Keep the complete Platform structure and consumer-owned `HarborlinePlatformSourceRoot` with `<ProjectReference Include="$(HarborlinePlatformSourceRoot)/projections/blazor/ui/hlp.ui.button/Harborline.UIAdapters.Blazor.csproj" />`. Read-only setup: `projections/blazor/ui/hlp.ui.app-shell/rev6/FRAME-USAGE.md`. No new package/repository/host, publication, MAUI or separate-server certification.

Namespaces: `Harborline.UIAdapters.Blazor.Components.DataDisplay` (ViewRuntime/Chart), `.Components.AI` (Chat), `.Components.Scheduling` (Scheduler). Under `_content/Harborline.UIAdapters.Blazor/`: ViewRuntime uses its uniquely mapped `style.css` plus `data-grid.css`; Chart `chart.css`; Chat `chat.css`; Scheduler `scheduler.css`, `dialog.css`, `scroll-affordance-component.css`. style.css is not aggregate CSS. Chat/Scheduler auto-import `chat.js`/`scheduler-now.js`. Scheduler needs `using Harborline.UIAdapters.Blazor;` and `builder.Services.AddHarborlineUiAdapters();`. No shared setup edits.

At ba8 the lead adopted a fresh same-Mac build and 20 Blazor+18 React bounded desktop/narrow/owner/dirty-close/form observations: not 38 tests, Intel/backend/future-edit certification. Failures/limits remain preserved. Return **BROWSER_PENDING**; lead owns fresh changed-source packages/stamp/gallery, loaded-asset joins and desktop+narrow keyboard/focus/locale/reflow. Old archives/galleries cannot qualify edits; no freshness skip/new server.

## Evidence, reviews and finite return

For each slice: evaluate baseline → actual regression red → minimal repair → focused green. Setup/compile errors are not behavioral red. Already passing means preserve/report coverage. Tests must run production behavior with literal/spec/neutral oracles independent of production constants/output (AGENTS.md); preserve original assertions/design.

Private commits/hooks only; no shared push/merge. Independent Spec and Standards/adversarial review if available; otherwise **REVIEW_PENDING**. Lead owns protected integration/CI.

Log UTC checkpoints for reconciliation, Scheduler, Chart/Chat and return; no four-day promise. Return baseline/document revision, commits/patches, path/hashes, requirements/oracles, command exits/logs/TRX/counts, preserved red/green, reviews, SETUP_BLOCKED and residual proposals. Name unreached browser cases and **BROWSER_PENDING**. Human-started finite source work is authorized; unrelated/shared work, publication and other-machine actions are excluded.

After these slices, triage the remaining non-v6 catalog metadata and return prioritized implementation/evidence candidates with exact requirements, paths and next checks. Held families remain metadata-only and excluded from implementation. Do not wait for a response to continue another available unit within this assignment; changes outside the listed writers remain precise proposals.
