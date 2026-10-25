# T-962 keyboard tab walk: shard time and seeded proofs, 2026-10-25

T-962 adds a keyboard tab walk to every gallery scenario test, in both lanes (`assertTabWalk` in
`gallery/tests/tests/gallery.spec.ts`, engine in `gallery/tests/tests/tab-walk.ts`). This record
holds the measured cost of that walk on the `gallery-accessibility-and-parity` step, the full gate at
the branch head, and the one-off seeded proofs run against the real gallery. Acceptance 4 of T-962
asks for the added shard time. R-0125 B5 sets the rule: if a shard passes 810 000 ms (90% of the
900 000 ms step budget), add a matrix shard; never move the probe.

## Commits and host

- Base: `ebe06e05`, T-943 (ARIA-tree parity, draft PR #344), which T-962 stacks on.
- Head: `63473cae`, the last T-962 code commit. This record is added by the next commit, which
  changes no code.
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
  380 ms on each Tab press with 256 events, recorded as a deadline finding owned by T-146),
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

## The full gate at the head

The full matrix (three shards and the collector) at `63473cae` passed: PASS, 599 browser tests
expected and passed, 0 unexpected, 0 flaky, with exact scenario reconciliation. Over the 481 running
scenarios it declared and reconciled `tabWalkLeaveChecks`, `tabWalkReverseChecks`,
`tabWalkObscuredChecks` and `tabWalkOrderComparisons` (481 each) and `tabWalkDetectorProofs` (10).
The walk visited 2130 React stops and 1808 Blazor stops forward, and as many in reverse. Modals
released focus 28 times in each lane. Nine stop visits in each lane were covered only by content the
user opened, which WCAG 2.2 Understanding 2.4.11 exempts; they are counted, not failed. In 40
scenarios the two lanes' orders differed only where T-943's ARIA register already waives a role, name
or state difference, so the walk attributed the difference to that row instead of registering it
twice.

## Seeded proofs in the real gallery

Each seed was applied in the working tree only, to the React probe of `button.variants` in
`gallery/projections/react/src/Button.stories.tsx`, and run through the real gate on the alternate
ports at `63473cae`:

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

`git status --short gallery/projections` was empty after the control run. The ten permanent detector
proofs (P1 to P10 in `gallery.spec.ts`) run in every gate without servers and are counted as
`tabWalkDetectorProofs: 10`.

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
  (9 cases).
- **Detail panel fixture** (gallery story): both lanes passed a constant `open` with no close
  callback, so the modal sheet could not be dismissed. The fixtures now own the open state.

`gallery/tab-walk-known-divergences.json` holds 54 rows over 41 scenarios: 39 order rows recorded as
hunks and 15 findings rows. No row names T-962 or T-943, the tickets that build the two registers:

| Owner | Rows | Defect |
|---|---|---|
| T-1104 (filed with T-943) | 6 | The text-box stories render different fields; the Blazor dialog story's labels are not localized and it lacks the replacement control |
| T-146 (existing) | 11 | The Blazor scheduler is a different component; with 256 events its own focus handling costs about 380 ms a Tab press, so the walk reports a deadline |
| T-1118 (proposed) | 18 | Blazor Chat message articles cancel every keydown: a 2.1.2 trap (Chat, out of T-962's scope) |
| T-1119 (proposed) | 9 | React's Gantt makes its first task cell a tab stop that Blazor's grid lacks (Gantt, out of scope) |
| T-1120 (proposed) | 1 | The pseudo-locale Gantt time scale combobox is clipped when focused (Gantt, out of scope) |
| T-1121 (proposed) | 1 | AppShell dock panels cover each other's controls (AppShell, out of scope) |
| T-1122 (proposed) | 2 | The Dialog surface's right-to-left centring puts its buttons offscreen, in both lanes |
| T-1123 (proposed) | 5 | The window's header and resize handles differ between lanes; the window is outside the probe, so neither axe nor T-943 reads it |
| T-1124 (proposed) | 1 | Blazor components that still render tabindex from an int (the Arabic Gantt loses focus) |

The proposed tickets are drafted for filing with this change, and the rows name them so the register
is valid when they land. The rows were measured on one host. A findings row has no host unless a
second host shows it differs. Occlusion findings are the likeliest to need a host-scoped row on Linux
CI.
