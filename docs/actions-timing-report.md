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
For a cancelled job with `runner_id: 0` and no steps, `started_at` can be a server
placeholder. The report records `cancelledWithoutRunner: true` and
`timeToCancellationMs` from creation to cancellation, with waiting/execution null.
An assigned cancelled job retains its observed execution. The cancellation outcome
is preserved in both cases; no-runner cancellations cannot enter an execution path.

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
node tooling/actions-timing-report.mjs run.json jobs.json needs.json gate.json JOB_ID checkout.json
```

Download `gate.json` from the source job's uploaded phase-4 artifact. Gate input
requires a caller-verified checkout record, not just the REST head SHA: on a PR,
REST names the PR head while default checkout tests its synthetic merge. Read the
**exact source job's checkout log**, then retrieve that commit's tree and ordered
parents from git or GitHub's Git commit API. Verify those sources and the artifact's
run/attempt/job before setting `verifiedByCaller: true`. An example record:

```json
{
  "verifiedByCaller": true,
  "runId": 36803894385,
  "runAttempt": 1,
  "jobId": 110183939270,
  "event": "pull_request",
  "headSha": "3722601bd1142031b965a13e07e772842f339e37",
  "sourceJobUrl": "https://github.com/Harborline-Software/harborline-platform/actions/runs/36803894385/job/110183939270",
  "checkoutSha": "f6ee8ad943f9db1abc05477301fe7275d28f8c34",
  "checkoutTree": "51e1b2f3217e73056dce8dbf53dceba43ecec799",
  "parents": ["de97dfc12a28d75fa3e450522d7eba6b561e2126", "3722601bd1142031b965a13e07e772842f339e37"]
}
```

The reporter binds run, attempt, job, event, REST head and source job URL, then
requires the gate `baseHead`/`testedTree` to equal the verified checkout commit/tree.
A PR synthetic merge must name the REST PR head as its second parent. An explicit
PR head checkout is also supported. Other events must check out the REST head.
Wrong trees, merge parents, event identities, stale attempts and missing verification
are refused. No checkout ref is fetched live by this offline reporter: mutable PR
merge refs could now identify a different tree.

`checkoutRecordMatched: true` means consistency checks passed. It does **not**
authenticate caller-supplied records: `checkoutEvidenceAuthenticated: false` and
`artifactJobAndAttemptVerified: false` preserve that trust boundary. The caller must
verify downloaded log/commit and artifact provenance against the stated job. Gate
subjects, status, successful step timing, failures and `reusedFrom` remain intact.
Nested durations overlap parents; never sum the ranked list. No report can turn a
failed or skipped check into a pass. These measurements are not an individual or
team performance signal.
