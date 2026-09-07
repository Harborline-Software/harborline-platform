// Ticket 277: when the phase-4 gate fails, the receipt used to throw with only the last 240
// lines of the gate's JSON report -- almost always the tail of passing steps, with the failing
// step's own output sitting above the cut. This module walks the full report (including grouped
// steps like `native-tests`, whose `report.results` holds one entry per project) and names every
// failed leaf step directly in the thrown message, so the operator never has to re-run an
// 18-minute gate just to learn which step failed.

const HEAD_LINES = 20

// A step is a "spawn failure" when the process never started. No producer in this repo attaches
// an `error` object to a step entry -- spawnSync-based steps (run-phase-4-gate.mjs) record
// exitCode: null with stdout/stderr coerced to the literal strings "null"/"undefined" by the
// template literal that builds failureOutput; spawn()-based steps (run-native.mjs, run-shared.mjs)
// record exitCode: -1 with the real Node spawn error (e.g. "spawn dotnet ENOENT") folded into
// stderr. Both are keyed on the exit code, never on a field nothing emits.
function isSpawnFailure(step) {
  return step.exitCode === null || step.exitCode === undefined || step.exitCode === -1
}

// The spawnSync placeholder shape carries no real output -- printing it verbatim would put a
// literal "undefined"/"null" line in the operator-facing message. Suppress only that placeholder;
// a real stderr stack (the run-native/run-shared shape) is left untouched.
function isPlaceholderOutput(output) {
  return output === 'null\nnull' || output === 'undefined\nundefined'
}

function firstLines(text) {
  return (text ?? '').split('\n').slice(0, HEAD_LINES).join('\n')
}

// Recursively collects every FAILED leaf step. A step with a nested `report.results` array (a
// grouping step such as `native-tests`) is not reported itself -- its failed children are, since
// those carry the actual command and output the operator needs. If a grouping step is marked
// failed but none of its children are, the group itself is reported as a fallback so a failure is
// never silently dropped.
export function collectFailedSteps(results) {
  const failed = []
  for (const step of results ?? []) {
    const nested = step.report?.results
    if (Array.isArray(nested)) {
      const childFailures = collectFailedSteps(nested)
      if (childFailures.length > 0) {
        failed.push(...childFailures)
        continue
      }
      if (step.passed === false) failed.push(step)
      continue
    }
    if (step.passed === false) failed.push(step)
  }
  return failed
}

function formatStep(step) {
  const command = (step.command ?? []).join(' ')
  if (isSpawnFailure(step)) {
    const raw = step.failureOutput ?? step.stderr
    const output = isPlaceholderOutput(raw) ? '' : firstLines(raw)
    const body = output ? `\n${output}` : ''
    return `${step.id}: exitCode=${step.exitCode} command: ${command}${body}`
  }
  const output = firstLines(step.failureOutput ?? step.stderr)
  return `${step.id}: exitCode=${step.exitCode} durationMs=${step.durationMs} command: ${command}\n${output}`
}

// Builds the message the receipt throws with: the report path first, then one block per failed
// step. Nothing else -- no passing steps, no full report inline. A gate can also fail on an
// invariant with every step passed (counts/reconciliation mismatch) -- name what tripped instead
// of the old silent "no step reported passed: false" line.
export function formatGateFailure(reportPath, report) {
  const failedSteps = collectFailedSteps(report.results)
  const lines = [`phase-4 gate report: ${reportPath}`]
  if (failedSteps.length === 0) {
    lines.push(`phase-4 gate failed on an invariant, not a step: counts=${JSON.stringify(report.counts ?? {})} scenarioReconciliation=${report.scenarioReconciliation}`)
  }
  for (const step of failedSteps) lines.push(formatStep(step))
  return lines.join('\n')
}
