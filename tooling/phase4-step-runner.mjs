const OUTPUT_CAP = 16 * 1024
const TRUNCATION_SUFFIX = `\n[truncated after ${OUTPUT_CAP} characters]`

function boundedOutput(value) {
  if (value === undefined || value === null) return ''
  const output = String(value)
  if (output.length <= OUTPUT_CAP) return output
  // The retained output, including the marker, is always at most OUTPUT_CAP characters.
  return `${output.slice(0, OUTPUT_CAP - TRUNCATION_SUFFIX.length)}${TRUNCATION_SUFFIX}`
}

export function boundedReport(report) {
  if (report === undefined) return undefined
  const serialized = JSON.stringify(report)
  // The persisted receipt needs bounded child output; the gate retains the live value separately
  // while it calculates its own summary fields.
  return serialized.length <= OUTPUT_CAP ? report : boundedOutput(serialized)
}

export function reportWasTruncated(report) {
  return typeof report === 'string' && report.endsWith(TRUNCATION_SUFFIX)
}

function errorDetails(error, result) {
  const resultStatus = result && Object.hasOwn(result, 'status') ? result.status : error?.status
  const details = {
    message: error instanceof Error ? error.message : String(error),
  }
  for (const [name, value] of Object.entries({
    code: error?.code,
    errno: error?.errno,
    signal: result?.signal ?? error?.signal,
    status: resultStatus,
  })) {
    if (value !== undefined) details[name] = value
  }
  return details
}

function failureOutput(stdout, stderr, message) {
  return [stdout, stderr, message].filter(Boolean).join('\n').split('\n').slice(-80).join('\n')
}

// Kept separate from the gate orchestration so a real spawnSync launch exception can be injected
// by a self-test. The gate still owns sequencing and stops on the first failed required step.
export function runPhase4Step({
  results,
  id,
  executable,
  args,
  cwd,
  json,
  env,
  execute,
}) {
  const started = performance.now()
  const command = [executable, ...args]
  let result
  let recorded = false
  try {
    const execution = execute()
    result = execution.result
    if (result.error) throw result.error
    const outcome = execution.outcome
    const report = outcome.report
    result.status = outcome.status
    if (outcome.failure) result.stderr = [result.stderr, `${id}: ${outcome.failure}`].filter(Boolean).join('\n')
    const entry = {
      id,
      command,
      exitCode: result.status,
      durationMs: Math.round(performance.now() - started),
      passed: result.status === 0,
      report: boundedReport(report),
      failureOutput: result.status === 0 ? undefined : failureOutput(boundedOutput(result.stdout), boundedOutput(result.stderr)),
    }
    results.push(entry)
    recorded = true
    if (!entry.passed) throw new Error(`${id} failed`)
    return report
  } catch (error) {
    if (recorded) throw error
    const stdout = boundedOutput(result?.stdout)
    const stderr = boundedOutput(result?.stderr)
    const details = errorDetails(error, result)
    const status = result && Object.hasOwn(result, 'status') ? result.status : error?.status
    results.push({
      id,
      command,
      exitCode: status === null ? null : (typeof status === 'number' && status !== 0 ? status : 1),
      durationMs: Math.round(performance.now() - started),
      passed: false,
      error: details,
      stdout,
      stderr,
      failureOutput: failureOutput(stdout, stderr, details.message),
    })
    throw error
  }
}

export function gateErrorDetails(error) {
  return errorDetails(error)
}

export function interruptionDetails(results, requiredStepIds) {
  if (results.length === requiredStepIds.length) return {}
  const completedIds = new Set(results.map(result => result.id))
  const failed = results.find(result => result.passed === false)
  const stoppedAt = failed?.id ?? requiredStepIds.find(id => !completedIds.has(id))
  const stoppedAtIndex = requiredStepIds.indexOf(stoppedAt)
  return {
    stoppedAt,
    unreachedRequiredStepIds: stoppedAtIndex === -1
      ? requiredStepIds.filter(id => !completedIds.has(id))
      : requiredStepIds.slice(stoppedAtIndex + 1).filter(id => !completedIds.has(id)),
  }
}
