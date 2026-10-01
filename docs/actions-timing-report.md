# Actions timing evidence

`tooling/actions-timing-report.mjs` is a report-only companion to the existing gate
failure reporter. It reads REST exports and the gate's existing duration evidence.
It does not judge a gate, change its conclusions, or write a receipt. T-957 owns
delivery metrics and T-963 owns the weekly quality report in control; this JSON can
be an input to those collectors without introducing another dashboard. T-679 owns
runner attribution and T-104 owns reuse; this reporter preserves both kinds of evidence.

Prior art: GitHub's documented [workflow job REST endpoints](https://docs.github.com/en/rest/actions/workflow-jobs)
and [workflow run REST endpoints](https://docs.github.com/en/rest/actions/workflow-runs),
using their attempt-specific resources and original status/timestamp fields. The
critical execution path is the standard longest path through an explicitly supplied
directed acyclic dependency graph. No external dashboard or retry policy is added.

Export one **specific attempt**, including every job page, using authenticated `gh`:

```sh
gh api repos/Harborline-Software/harborline-platform/actions/runs/RUN/attempts/ATTEMPT > run.json
gh api --paginate --slurp 'repos/Harborline-Software/harborline-platform/actions/runs/RUN/attempts/ATTEMPT/jobs?per_page=100' > jobs.json
node tooling/actions-timing-report.mjs run.json jobs.json > timing.json
```

Repeat for each attempt to retain failed and cancelled runs alongside reruns. Fetch
the run again if it is still executing. The report rejects jobs from another run,
attempt or SHA. Supply complete REST exports; job pagination and artifact provenance
remain the caller's responsibility. `run_attempt > 1` records a rerun, not a flaky
test or a step retry. Step outcomes, including skipped and cancelled, remain visible.

Job `observableWaitMs` is `created_at` to `started_at`; it may include dependency or
orchestration delay, not exclusively runner congestion. `attemptStartToJobCreatedMs`
reports the delay before job creation separately. Neither proves runner saturation.
Execution is `started_at` to `completed_at`. Null means unknown,
including unfinished or reversed timestamps. Workflow `updated_at` is not a finish
timestamp. `attemptObservedWallMs` starts at this attempt's `run_started_at` and ends
at the last executed job, only for completed runs with complete execution timestamps.
Skipped-job and skipped-step timestamps are bookkeeping and never count as waiting
or execution. `runLifetimeToLastJobMs` starts at original run creation and can include
previous attempts and idle time. `runCreatedToAttemptStartMs` has the same limitation.
Do not sum reports for overlapping attempts. Wall time excludes final workflow bookkeeping.

REST jobs do not carry `needs`. For a critical execution path, supply an explicit
JSON graph whose keys are every exact REST job name (including matrix expansions)
and whose values are arrays of prerequisite job names. Read the workflow **at the
reported SHA**. An incomplete, cyclic, unknown or timestamp-contradicting graph is refused.
`dependenciesVerified: false` explicitly records that the tool cannot detect omitted
real dependencies or independently confirm the graph against the workflow. The longest path
sums execution only; it excludes waiting and does not claim to be elapsed workflow
time. Without a graph, the report gives the last completing job and marks the
critical path unavailable.

```sh
node tooling/actions-timing-report.mjs run.json jobs.json needs.json
node tooling/actions-timing-report.mjs run.json jobs.json needs.json gate.json JOB_ID
```

Download `gate.json` from the source job's uploaded phase-4 artifact. An optional
gate report retains its own status and subject (`baseHead`/`testedTree`); these are
not replaced with the Actions SHA. Its `baseHead` must match the Actions SHA; a stale
or unpinned report is refused. Successful step timing is retained alongside
failures and `reusedFrom`. Nested durations overlap their parents, so never sum the
ranked list. The tool checks that JOB_ID belongs to the run; the caller must verify
that the artifact came from that job and attempt and that `testedTree` is the tree
actually tested. Those unverified properties are explicit in `provenance`. No report can turn a failed or
skipped check into a pass. These measurements are not an individual or team
performance signal.
