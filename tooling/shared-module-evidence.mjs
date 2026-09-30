// Builds the evidence a failed ui-shared-conformance module records.
//
// A module runner prints its whole report as pretty JSON and exits 1 when any case fails. The
// evidence used to keep only the last 80 lines of that stdout, which is the tail of the results
// array: on platform PR 181 hlp.ui.data-grid and hlp.ui.chat failed with every visible case
// reading "passed": true, because the failing case sat above the cut. The report itself names
// the failing cases, so it is parsed whatever the exit code, and the evidence records those
// entries rather than a longer raw tail. The raw tail stays only as the fallback for a runner
// that printed no parseable report (a throw before the report, a crashed host).

export const CASE_OUTPUT_LINES = 20
export const RAW_TAIL_LINES = 80
export const STDERR_TAIL_LINES = 40

function tail(text, lines) {
  return String(text ?? '').trimEnd().split('\n').slice(-lines).join('\n')
}

export function parseModuleReport(stdout) {
  try {
    const report = JSON.parse(stdout)
    return report && typeof report === 'object' && !Array.isArray(report) ? report : undefined
  } catch {
    return undefined
  }
}

export function failingCases(report) {
  return (report?.results ?? [])
    .filter(entry => entry && entry.passed !== true)
    .map(entry => ({
      caseId: entry.caseId,
      projection: entry.projection,
      command: entry.command,
      exitCode: entry.exitCode,
      testCount: entry.testCount,
      outputTail: entry.failureOutput === undefined ? undefined : tail(entry.failureOutput, CASE_OUTPUT_LINES),
    }))
}

// One attempt's diagnosis: the failing cases its report names, its stderr, and the raw stdout
// tail only when the report cannot say which case failed.
export function describeAttempt({exitCode, durationMs, stdout, stderr}) {
  const report = parseModuleReport(stdout)
  const failures = failingCases(report)
  const description = {exitCode, durationMs, reportStatus: report?.status, failures}
  if (String(stderr ?? '').trim() !== '') description.stderrTail = tail(stderr, STDERR_TAIL_LINES)
  if (failures.length === 0) description.stdoutTail = tail(stdout, RAW_TAIL_LINES)
  return description
}

// The evidence fields tooling/run-shared.mjs records for one module. `result` is one execute()
// outcome, carrying `attempts` and, when the one retry happened, the complete first execution as
// `firstAttempt`. The first attempt is described even when the retry passed: a case that failed
// once and cleared on retry is the flake the retry would otherwise hide.
export function moduleEvidence(result, passed) {
  const evidence = {attempts: result.attempts}
  if (result.firstAttempt) evidence.firstAttempt = describeAttempt(result.firstAttempt)
  if (!passed) {
    const report = parseModuleReport(result.stdout)
    evidence.counts = report?.counts
    evidence.failureOutput = JSON.stringify(describeAttempt(result), null, 2)
  }
  return evidence
}
