import {mkdirSync, writeFileSync} from 'node:fs'
import {dirname} from 'node:path'

const note = 'Captured, not judged. No budget exists yet; ticket 098 defines this gate as capture until explicit budgets are set.'

export function writeUiPerformanceProfile(outputPath, reports) {
  if (reports.some(report => report.executions.some(execution => execution.exitCode))) {
    // A failed test duration describes the failure path, so recording it would make unavailable
    // measurement infrastructure look like valid client performance evidence.
    throw new Error('cannot emit UI performance profile because one or more profile executions failed')
  }

  const modules = Object.fromEntries(reports.map(report => [
    report.moduleId,
    Object.fromEntries(report.executions.map(execution => [
      `${execution.id}DurationMs`,
      execution.durationMs,
    ])),
  ]))
  const profile = {schemaVersion: 1, note, modules}

  mkdirSync(dirname(outputPath), {recursive: true})
  writeFileSync(outputPath, `${JSON.stringify(profile, null, 2)}\n`)
}
