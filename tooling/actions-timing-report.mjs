#!/usr/bin/env node
// Report-only companion to gate-failure-report.mjs. Reads exported REST evidence;
// never changes a check conclusion, re-runs a job, or writes gate receipts.
import {readFileSync} from 'node:fs'

function instant(value) {
  if (!value) return null
  const parsed = Date.parse(value)
  return Number.isFinite(parsed) ? parsed : null
}

function interval(start, end) {
  const a = instant(start), b = instant(end)
  return a === null || b === null || b < a ? null : b - a
}

function ranked(rows) {
  return rows.filter(row => row.executionMs !== null)
    .sort((a, b) => b.executionMs - a.executionMs)
}

// A graph is keyed by exact REST job name, including each matrix expansion.
// REST has no `needs` field. Refuse to invent dependencies from timestamp order.
function criticalPath(jobs, dependencies) {
  if (jobs.length === 0) return {available: false, reason: 'No observed jobs.'}
  if (!dependencies) return {available: false, reason: 'Supply an explicit dependency graph from the workflow at headSha.'}
  const byName = new Map(jobs.map(job => [job.name, job]))
  if (byName.size !== jobs.length) throw new Error('Dependency graph requires unique job names')
  if (Object.keys(dependencies).length !== jobs.length || jobs.some(job => !Object.hasOwn(dependencies, job.name))) {
    throw new Error('Dependency graph must cover every observed job exactly')
  }
  const visiting = new Set(), paths = new Map()
  function visit(name) {
    if (paths.has(name)) return paths.get(name)
    if (visiting.has(name)) throw new Error('Dependency graph contains a cycle')
    const job = byName.get(name)
    if (!job || !Array.isArray(dependencies[name])) throw new Error(`Unknown or invalid dependency: ${name}`)
    visiting.add(name)
    const parents = dependencies[name].map(visit)
    for (const parentName of dependencies[name]) {
      const parent = byName.get(parentName)
      if (parent.executionMs !== null && job.executionMs !== null
          && instant(job.startedAt) < instant(parent.completedAt)) {
        throw new Error(`Dependency timestamps overlap: ${parentName} -> ${name}`)
      }
    }
    if (job.executionMs === null || parents.some(path => path === null)) {
      visiting.delete(name)
      paths.set(name, null)
      return null
    }
    const longest = parents.sort((a, b) => b.executionMs - a.executionMs)[0]
    const path = {jobIds: [...(longest?.jobIds ?? []), job.id], jobNames: [...(longest?.jobNames ?? []), name],
      executionMs: (longest?.executionMs ?? 0) + job.executionMs}
    visiting.delete(name)
    paths.set(name, path)
    return path
  }
  const all = jobs.map(job => visit(job.name))
  if (all.some(path => path === null)) return {available: false, reason: 'Incomplete execution timestamps; no complete critical path.'}
  return {available: true, dependenciesVerified: false,
    basis: 'Longest execution-duration path through caller-supplied graph; actual workflow dependencies are not independently verified; excludes waiting.',
    ...all.sort((a, b) => b.executionMs - a.executionMs)[0]}
}

export function buildActionsTimingReport({run, jobs, dependencies, gateReports = []}) {
  if (!run?.id || !run.head_sha || !Number.isInteger(run.run_attempt)) throw new Error('Run id, head_sha and run_attempt are required')
  if (!Array.isArray(jobs)) throw new Error('Jobs must be an array')
  const ids = new Set()
  for (const job of jobs) {
    if (!job.id || ids.has(job.id)) throw new Error('Job ids must be present and unique')
    ids.add(job.id)
    if (job.run_id !== run.id || job.run_attempt !== run.run_attempt || job.head_sha !== run.head_sha) {
      throw new Error(`Job ${job.id} does not belong to this exact run, attempt and SHA`)
    }
  }
  const rows = jobs.map(job => ({id: job.id, name: job.name, url: job.html_url,
    status: job.status, conclusion: job.conclusion, attempt: job.run_attempt,
    createdAt: job.created_at, startedAt: job.started_at, completedAt: job.completed_at,
    observableWaitMs: job.conclusion === 'skipped' ? null : interval(job.created_at, job.started_at),
    attemptStartToJobCreatedMs: interval(run.run_started_at, job.created_at),
    executionMs: job.conclusion === 'skipped' ? null : interval(job.started_at, job.completed_at),
    runner: {id: job.runner_id, name: job.runner_name, labels: job.labels},
    steps: (job.steps ?? []).map(step => ({number: step.number, name: step.name,
      status: step.status, conclusion: step.conclusion,
      executionMs: step.conclusion === 'skipped' ? null : interval(step.started_at, step.completed_at)})),
  }))
  const gates = gateReports.map(({jobId, report}) => {
    if (!ids.has(jobId)) throw new Error(`Gate report references unknown job ${jobId}`)
    if (report.subject?.baseHead !== run.head_sha) throw new Error(`Gate report for job ${jobId} does not name the Actions head SHA`)
    // Keep the gate's own subject separate: baseHead/testedTree are not the Actions head SHA.
    const steps = []
    function walk(results, parents = []) {
      for (const step of results ?? []) {
        const path = [...parents, step.id]
        steps.push({path, passed: step.passed, exitCode: step.exitCode,
          executionMs: Number.isFinite(step.durationMs) && step.durationMs >= 0 ? step.durationMs : null,
          reusedFrom: step.reusedFrom ?? null})
        if (Array.isArray(step.report?.results)) walk(step.report.results, path)
      }
    }
    walk(report.results)
    return {jobId, status: report.status, subject: report.subject,
      provenance: {baseHeadMatchesActionsSha: true, testedTreeVerified: false, artifactJobAndAttemptVerified: false},
      steps, slowSteps: ranked([...steps])}
  })
  const executed = rows.filter(row => row.conclusion !== 'skipped')
  const completed = executed.filter(row => row.status === 'completed' && row.executionMs !== null)
    .sort((a, b) => instant(b.completedAt) - instant(a.completedAt))
  const end = run.status === 'completed' && completed.length === executed.length && completed.length > 0 ? completed[0].completedAt : null
  return {schemaVersion: 1, reportOnly: true,
    run: {id: run.id, url: run.html_url, headSha: run.head_sha, event: run.event,
      status: run.status, conclusion: run.conclusion, attempt: run.run_attempt,
      retried: run.run_attempt > 1, previousAttemptCount: run.run_attempt - 1,
      createdAt: run.created_at, startedAt: run.run_started_at,
      runCreatedToAttemptStartMs: interval(run.created_at, run.run_started_at),
      runLifetimeToLastJobMs: interval(run.created_at, end),
      attemptObservedWallMs: interval(run.run_started_at, end)},
    caveats: ['Job observable waiting is created_at to started_at; it can include dependency/orchestration delay and is not exclusively runner queue time or proof of saturation.',
      'Time before job creation is measured from this attempt start and includes dependency/orchestration delay.',
      'Run creation is shared across reruns: run lifetime and creation-to-attempt-start can include previous attempts and idle time. Do not sum overlapping attempt reports.',
      'Skipped job and step timestamps are bookkeeping, not execution or waiting evidence.',
      'Null duration means missing, incomplete or reversed timestamps, never zero.',
      'Attempt count records reruns, not automatic step retries. Fetch every attempt separately.',
      'updated_at is metadata update time, not execution completion.',
      'Gate baseHead must match Actions head SHA; testedTree and artifact job/attempt provenance remain caller-verified. Nested durations overlap their parent and must not be summed.',
      'These measurements are not an individual or team performance signal.'],
    jobs: rows, slowSteps: ranked(rows.flatMap(job => job.steps.map(step => ({jobId: job.id, jobName: job.name, ...step})))),
    criticalExecutionPath: criticalPath(rows, dependencies),
    lastCompletingJob: completed.length ? {id: completed[0].id, name: completed[0].name, completedAt: completed[0].completedAt} : null,
    gateReports: gates}
}

if (import.meta.main) {
  try {
    const [runPath, jobsPath, graphPath, gatePath, gateJobId] = process.argv.slice(2)
    if (!runPath || !jobsPath) throw new Error('Usage: node tooling/actions-timing-report.mjs RUN.json JOBS.json [NEEDS.json|-] [GATE.json JOB_ID]')
    const read = path => JSON.parse(readFileSync(path, 'utf8'))
    const exported = read(jobsPath)
    const pages = Array.isArray(exported) ? exported : [exported]
    if (!pages.length || pages.some(page => !Array.isArray(page.jobs) || !Number.isInteger(page.total_count))) {
      throw new Error('Expected REST jobs pages with jobs and total_count')
    }
    const jobs = pages.flatMap(page => page.jobs)
    if (pages.some(page => page.total_count !== jobs.length)) throw new Error('Incomplete or inconsistent job pages; fetch all pages for this attempt')
    if (gatePath && !gateJobId) throw new Error('GATE.json requires its source JOB_ID')
    process.stdout.write(`${JSON.stringify(buildActionsTimingReport({run: read(runPath), jobs,
      dependencies: graphPath && graphPath !== '-' ? read(graphPath) : undefined,
      gateReports: gatePath ? [{jobId: Number(gateJobId), report: read(gatePath)}] : []}), null, 2)}\n`)
  } catch (error) {
    process.stderr.write(`${error.message}\n`)
    process.exitCode = 1
  }
}
