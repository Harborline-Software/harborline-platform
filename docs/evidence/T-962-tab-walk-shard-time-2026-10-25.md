# T-962 keyboard tab walk: shard time and seeded proofs, 2026-10-25

T-962 adds a keyboard tab walk to every gallery scenario test, in both lanes (`assertTabWalk` in
`gallery/tests/tests/gallery.spec.ts`, engine in `gallery/tests/tests/tab-walk.ts`). This record
holds the measured cost of that walk on the `gallery-accessibility-and-parity` step, the full gate at
the branch head, and the one-off seeded proofs run against the real gallery. Acceptance 4 of T-962
asks for the added shard time. R-0125 B5 sets the rule: if a shard passes 810 000 ms (90% of the
900 000 ms step budget), add a matrix shard; never move the probe.

## Commits and host

- Base: `ebe06e05`, T-943 (ARIA-tree parity, draft PR #344), which T-962 stacks on.
- Head: `63473cae`, the last T-962 code commit before review 1. This record was added by the next
  commit, which changed no code. The review 1 fixes (`75cf1b86` to `8f24a8a1`), the review 2 fixes
  (`f5d5e7b9` to `39c6fa23`), the review 3 fixes (`f35c8c01`) and the review 4 fixes (`dfc67b00`) are
  measured in the later parts of each section below.
- Host: macos-x64-intel (Intel Core i9-9880H, 8 cores and 16 threads), shared with other agents.
  Node 24.19.0, Playwright 1.62.1, React gallery on port 6116 and Blazor gallery on port 6117.
- Each shard ran with `config.workers = 4`. Nobody else held the full-gate lock during any run. Every
  shard started with a one-minute load average under 40, and the load at the start and end of each
  shard is in its run's `load.txt`.

## Measurement

Base, then head, twice, alternating. In each run the three matrix shards ran one after another, then
the collector merged them:

```text
HARBORLINE_GALLERY_REACT_PORT=6116 HARBORLINE_GALLERY_BLAZOR_PORT=6117 \
  node tooling/run-gallery-gate.mjs --packages-ready --shard=i/3 > <run>/gallery-shard-i.json
node tooling/run-gallery-gate.mjs --merge=<run>
```

The step time is `results[gallery-accessibility-and-parity].durationMs` from each shard's report, in
milliseconds; Playwright's own `stats.duration` is within 1.4 s of it in every shard. The walk wall
is the sum, over the shard's scenarios, of the walk's own wall time from the `hlp-tab-walk`
annotation (the collector's `tabWalkByShard`). Four workers share it.

| Shard | Base run 1 | Base run 2 | Head run 1 | Head run 2 | Head mean − base mean | Head walk wall (run 1 / run 2) |
|---|---:|---:|---:|---:|---:|---:|
| 1 | 419 360 | 396 155 | 402 516 | 364 385 | −24 307 | 141 549 / 134 394 |
| 2 | 491 212 | 472 557 | 519 680 | 537 956 | +46 934 | 176 227 / 183 713 |
| 3 | 368 083 | 368 583 | 403 563 | 384 717 | +25 807 | 68 461 / 70 628 |
| Sum | 1 278 655 | 1 237 295 | 1 325 759 | 1 287 058 | +48 434 | 386 237 / 388 735 |

Load averages (1, 5 and 15 minutes) at the start and end of each shard, in run order:

| Run | Commit | Shard 1 | Shard 2 | Shard 3 |
|---|---|---|---|---|
| Base run 1 | `ebe06e05` | 4.34/10.73/20.31 → 23.85/21.33/21.68 | 23.85/21.33/21.68 → 24.75/23.75/22.42 | 24.75/23.75/22.42 → 15.81/19.44/20.68 |
| Head run 1 | `63473cae` | 15.81/19.44/20.68 → 20.49/19.17/19.77 | 20.49/19.17/19.77 → 16.83/18.50/18.93 | 16.83/18.50/18.93 → 14.50/19.92/19.80 |
| Base run 2 | `ebe06e05` | 14.50/19.92/19.80 → 19.93/20.55/19.93 | 19.93/20.55/19.93 → 22.17/23.23/21.58 | 22.17/23.23/21.58 → 16.91/20.35/20.80 |
| Head run 2 | `63473cae` | 16.91/20.35/20.80 → 17.01/18.81/19.73 | 17.01/18.81/19.73 → 20.34/18.56/18.78 | 20.34/18.56/18.78 → 15.42/19.69/19.44 |

All twelve shards and all four merges passed: 582 browser tests at the base and 599 at the head,
0 flaky, exact scenario reconciliation.

- **Added time.** The walk adds about 48 s to the step summed over the three shards, 3.9% of the
  base, or about 16 s a shard on average. The sum is the figure to read. The head adds 17 browser
  tests (the walk's pure tests and detector proofs), so Playwright's shard boundaries move between
  base and head, and a per-shard delta mixes in tests that changed shard. Shard 1's negative delta is
  that plus run-to-run spread: the base's own three-shard sum differed by 41 s between its two runs.
- **The walk's own time** is about 387 s a run summed over scenarios, shared by four workers. Its
  slowest scenarios are `scheduler.large-data` (about 60.7 s, where the Blazor scheduler spends about
  380 ms on each Tab press with 256 events; this was recorded as a deadline finding, which review 1
  replaced with a press budget),
  `app-shell.actions-endpanel` (about 25.7 s; Blazor's AppShell takes about 160 ms per Tab and
  265 ms per Shift+Tab there) and `app-layout.performance` (about 9 s).
- **Budget.** The largest head shard took 537 956 ms (shard 2, run 2): 60% of the 900 000 ms budget
  and 272 044 ms below R-0125 B5's 810 000 ms line, so no shard is added. The full matrix at the head
  (below), a correctness run whose machine got busier during shard 2 (five-minute load 18.25 at its
  start and 41.67 at its end), took 584 646 ms on shard 2, 65% of the budget. That is the slowest
  head shard observed.
- **CI was not measured.** CI runs six shards with `PW_WORKERS: "2"` on Ubuntu. After the first
  merge-group run, the six CI shard figures come from the collector's `tabWalkByShard` and are added
  here.

### After review 1

The review fixes change what the walk does per scenario. The prepared walk is stepped all the way,
batched candidates are re-walked in step mode, and covered stops get an Escape. `scheduler.large-data`
stops at a 24-press budget instead of running into its deadline. So the cost was checked on the one
full matrix the fixes ran (`b9d5f298`, three shards then the collector, same host, ports and workers),
rather than by a new alternation:

| Shard | Step ms | Walk wall ms | Load 1/5/15 at start → end |
|---|---:|---:|---|
| 1 | 362 219 | 156 588 | 12.18/10.14/9.96 → 17.30/15.10/12.39 |
| 2 | 484 224 | 129 663 | 17.30/15.10/12.39 → 17.94/17.58/14.73 |
| 3 | 372 356 | 66 072 | 17.94/17.58/14.73 → 11.64/15.42/14.77 |
| Sum | 1 218 799 | 352 323 | |

The walk's own time summed over scenarios fell from about 387 s to 352 s. `scheduler.large-data`
went from about 60.7 s to 10.5 s, and `app-shell.actions-endpanel` rose from about 25.7 s to 49.2 s,
because its many obscured stops are now confirmed in step mode on a lane that takes about 160 ms a
press. The three-shard step sum (1 218 799 ms) is below both earlier head runs, and the largest shard
(484 224 ms) is 54% of the budget. The cost did not grow, so the alternation above was not repeated.

### After review 2

Review 2 judges every stop of the prepared walk and walks budgeted phases in step mode. It walks E for
an editing control at the first stop. It verifies user-opened covers once, after both directions,
instead of re-walking a phase. The one full matrix at `39c6fa23` ran on a busier machine:

| Shard | Step ms | Walk wall ms | Load 1/5/15 at start → end |
|---|---:|---:|---|
| 1 | 570 659 | 232 048 | 21.36/14.81/11.99 → 36.17/33.14/24.01 |
| 2 | 556 590 | 151 043 | 36.17/33.14/24.01 → 21.46/21.75/21.80 |
| 3 | 436 351 | 85 294 | 21.46/21.75/21.80 → 13.28/18.50/20.11 |
| Sum | 1 563 600 | 468 385 | |

The walk's work barely moved against `b9d5f298`: 2147 walks instead of 2136 (+0.5%) and 1389 step
presses instead of 1306 (+6%). Its wall time rose by a third, and the median scenario over 1.5 s took
1.18 times as long, which is the load (one-minute averages of 21 to 36 at shard boundaries against
12 to 18). So the cost did not change materially, and the alternation was not repeated. The largest
shard (570 659 ms) is 63% of the budget even under that load.

The test allowance is now sized from the census for the walks the engine can take: a stepped E when
either lane walks it, and a batched walk plus a stepped re-walk per direction (stepped only under a
budget). A step press is costed 40 ms more, with 30% on top. The annotation records each scenario's
allowance and the margin the slower lane left. The tightest scene in this run,
`app-shell.actions-endpanel`, took 69 997 ms of wall time against a 97 048 ms allowance and left
28 734 ms (70% used); before review 2 it used about 96%.

### After review 3

Review 3 tests each covered stop's own Escape and measures a stop again once its popup has closed.
It leaves inner scrollers alone when it restores the prepared focus, and keeps a budgeted phase in
step mode after a modal release. The one full matrix at `f35c8c01` ran on a quieter machine:

| Shard | Step ms | Walk wall ms | Load 1/5/15 at start → end |
|---|---:|---:|---|
| 1 | 410 002 | 181 991 | 4.19/6.49/6.80 → 21.67/17.47/12.18 |
| 2 | 522 895 | 135 595 | 21.67/17.47/12.18 → 17.35/18.05/15.01 |
| 3 | 393 361 | 72 467 | 17.35/18.05/15.01 → 15.24/17.57/15.90 |
| Sum | 1 326 258 | 390 053 | |

The walk did exactly the work it did at `39c6fa23` (2147 walks, 1389 step presses). The three-shard
step sum is level with the two earlier head runs (1 287 058 and 1 325 759 ms), and the largest shard
(522 895 ms) is 58% of the budget. The cost did not change materially. `app-shell.actions-endpanel` used 58.6% of its
allowance (37 122 ms left of 89 560 ms).

### After review 4

Review 4 judges a cover on its final measure: up to three Escapes while each closes the popups over
the stop with focus kept. It credits a popup only to the stop whose own Escape closed it. It gives E
back the exact window scroll the walk began with. The one full matrix at `dfc67b00`:

| Shard | Step ms | Walk wall ms | Load 1/5/15 at start → end |
|---|---:|---:|---|
| 1 | 504 875 | 210 198 | 14.11/11.38/7.92 → 21.81/40.23/29.47 |
| 2 | 487 680 | 123 733 | 21.81/40.23/29.47 → 19.21/23.07/24.95 |
| 3 | 379 160 | 73 570 | 19.21/23.07/24.95 → 13.68/18.24/21.81 |
| Sum | 1 371 715 | 407 501 | |

The walk did exactly the same work again (2147 walks, 1389 step presses). The step sum is 3.4% above
the review 3 run, on a busier machine (five-minute load up to 40 during shard 1). The largest shard
(504 875 ms) is 56% of the budget, so the cost did not change materially.
`app-shell.actions-endpanel` used 37.3% of its allowance.

## The full gate at the head

The full matrix (three shards and the collector) at `dfc67b00` passed: PASS, 604 browser tests
expected and passed, 0 unexpected, 0 flaky, with exact scenario reconciliation. Over the 481 running
scenarios it declared and reconciled `tabWalkLeaveChecks`, `tabWalkReverseChecks`,
`tabWalkObscuredChecks` and `tabWalkOrderComparisons` (481 each) and `tabWalkDetectorProofs` (14).
Three stops per lane were covered by a user-opened popup that Escape did not reveal in place. They
are the UserMenu trigger and the two AppShell pin buttons, all registered findings. No stop counted
as exempt.

At `f35c8c01` (review 3) and `39c6fa23` (review 2) the full matrix gave the same result. At `b9d5f298`, after review 1,
it passed with 603 browser tests, 0 flaky, exact reconciliation, thirteen proofs.
The walk visited 2130 React stops and 1808 Blazor stops forward, and as many in reverse. Modals
released focus 28 times in each lane. No stop counted as exempt under SC 2.4.11 Note 2. Before the
review the walk counted nine stop visits per lane as user-opened covers, in
`user-menu.placement-dismissal` and `app-shell.pins-threads`. Escape on those stops does not reveal
them, so they are now obscured findings with owning tickets (below). In 40 scenarios the two lanes'
orders differed only where T-943's ARIA register already waives a role, name or state difference, so
the walk attributed the difference to that row instead of registering it twice.

`8f24a8a1` puts SelectField's Blazor tabindex back to its approved form (its human design review
binds its render; T-1124 claims the fix). A focused `select-field` gate checked it, and the review 2
full matrix includes it.

The earlier full matrix at `63473cae` passed as well: 599 browser tests, 0 flaky, exact
reconciliation, ten proofs.

## Seeded proofs in the real gallery

Each seed was applied in the working tree only, to the React probe of `button.variants` in
`gallery/projections/react/src/Button.stories.tsx`, and run through the real gate on the alternate
ports. The seeds first ran at `63473cae`, before review 1. They ran again at `f35c8c01` after review
3, and at `dfc67b00`, the last engine commit after review 4. Every run gave the results below,
failing expect and finding lines included:

```text
HARBORLINE_GALLERY_REACT_PORT=6116 HARBORLINE_GALLERY_BLAZOR_PORT=6117 \
  GALLERY_GREP='\sbutton\.variants is accessible' node tooling/run-gallery-gate.mjs --packages-ready
```

| Seed | Gate | Failing expect, both attempts | Finding line |
|---|---|---|---|
| Tab on the last variant focuses the first (a focus trap) | FAIL | `button.variants tab walk focus leaves the scenario (WCAG 2.1.2)` | `react: 2.1.2 forward trap: ↻ button "primary" → … → button "transparent"` |
| Shift+Tab on the third variant skips the second | FAIL | `button.variants tab walk Shift+Tab retraces the order (WCAG 2.4.3)` | `react: 2.4.3 reverse differs: expected button "secondary" got button "primary"` |
| An opaque cover appears over the stage once focus enters it | FAIL | `button.variants tab walk stop is not obscured (WCAG 2.4.11)` | `react: 2.4.11 forward obscured: button "danger" under div.seed-cover`, the same for `transparent`, and both again in reverse |
| None (seed removed with `git restore`) | CHECKED | none | none |

`git status --short gallery/projections` was empty after the control run. The fourteen permanent
detector proofs (P1 to P14 in `gallery.spec.ts`) run in every gate without servers and are counted
as `tabWalkDetectorProofs: 14`. Review 1 changed P6 to P9 and added P11 to P13, and each fails on the
engine before that review. Review 2 extended P7, P11 and P13 with the reviewers' pages and added P14,
and each of those fails on the review 1 engine. Review 3 extended P7, P13 and P14 again, and each of
those fails on the review 2 engine. Review 4 extended P7 and P13 once more. With every expect made
soft, each new must-fail expect fails on the review 3 engine. The two re-measure expects pass there
and fail on a mutant that drops the re-measure (the review-fixes-4 evidence names each file).

## What the measured runs found

The full matrix run with `HARBORLINE_GALLERY_TAB_WALK_MEASURE` set, before any row was registered,
failed the same 41 scenarios over T-943 at `3c7a4de1` and at `460bb3d7`, each identically on both
attempts. Over `2590ed08`, where T-943 gives each scene its own browser locale and the Blazor gallery
loads all ICU data, three Arabic scenarios joined them. Three fixes landed instead of rows:

- **Blazor layers rail** (in scope): the outline tree's single tab stop started on the first node.
  React and the APG treeview pattern start it on the selected node, so all ten layers-rail scenarios
  walked different stops. Fixed in `HarborlineLayersRail.razor`; the bUnit test failed first.
- **Blazor roving tabindex under Arabic, Hebrew and Persian cultures** (in scope): Blazor writes an
  int attribute with the current culture. Under ar-SA, .NET writes -1 as U+061C U+002D U+0031, which
  HTML integer parsing refuses, so every non-current item of a roving group became a tab stop
  (`context-menu.locale-ar`, `layers-rail.locale-ar`, `segmented-control.locale-ar`). The three
  components now render "0" and "-1". The bUnit tests under ar-SA, he-IL and fa-IR failed first
  (9 cases). Review 1 widened this. The negative sign is not U+002D under sv, nb, fi, lt, sl, hr and
  eu either (they write U+2212), and decimals use a comma under de-DE, so CSS and HTML number parsing
  refuse them too. Every in-scope site now writes invariant values (ActionMenu, DataGrid, DetailPanel,
  SideNav, NumberField, and the ContextMenu position), each with a bUnit theory that failed first. The
  exceptions are UserMenu (the frozen round-2 batches change the same lines) and SelectField (its
  design review binds its render); T-1124 claims both.
- **Detail panel fixture** (gallery story): both lanes passed a constant `open` with no close
  callback, so the modal sheet could not be dismissed. The fixtures now own the open state.

`gallery/tab-walk-known-divergences.json` holds 56 rows over 43 scenarios: 39 order rows recorded as
position-anchored hunks, 16 findings rows and one budget row. Review 2 changed three of them: the
UserMenu and AppShell pin rows gained their prepared-walk lines, and the scheduler budget row's
walked prefix is now compared stop for stop. No row names T-962 or T-943, the tickets that build the
two registers. The new tickets are filed in control PR #1042:

| Owner | Rows | Defect |
|---|---|---|
| T-1104 (filed with T-943; amended) | 6 | The text-box stories render different fields; the Blazor dialog story's labels and close-button catalog are not localized and it lacks the replacement control |
| T-146 (existing; amended) | 11 | The Blazor scheduler is a different component; with 256 events its own focus handling costs about 380 ms a Tab press, so `scheduler.large-data` walks to a 24-press budget instead of in full |
| T-1118 (filed) | 18 | Blazor Chat message articles cancel every keydown: a 2.1.2 trap (Chat, out of T-962's scope) |
| T-1119 (filed) | 9 | React's Gantt makes its first task cell a tab stop that Blazor's grid lacks (Gantt, out of scope) |
| T-1120 (filed) | 1 | The pseudo-locale Gantt time scale combobox is clipped when focused (Gantt, out of scope) |
| T-1121 (filed) | 1 | AppShell dock panels cover each other's controls (AppShell, out of scope) |
| T-1122 (filed) | 2 | The Dialog surface's right-to-left centring puts its buttons offscreen, in both lanes (judged on the prepared walk, which reaches and releases the open dialog) |
| T-1123 (filed) | 5 | The window's header and resize handles differ between lanes; the window is outside the probe, so neither axe nor T-943 reads it |
| T-1124 (filed) | 1 | Blazor components that still write numeric attributes with the current culture (the Arabic Gantt loses focus) |
| T-1125 (filed) | 1 | The UserMenu panel does not close on Tab or focus loss and covers the second menu's trigger; Escape there does not reveal it |
| T-1126 (filed) | 1 | AppShell's thread row menu stays open on focus loss and covers the next stops (AppShell, out of scope) |

The new tickets (T-1118 to T-1126) are filed in control PR #1042, which also amends T-1104 and T-146. The rows were measured on one host. A findings row has no host unless a
second host shows it differs. Occlusion findings are the likeliest to need a host-scoped row on Linux
CI.
