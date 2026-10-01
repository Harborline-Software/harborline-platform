import {existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {join} from 'node:path'

// Counts never substitute for case evidence. Each result must come from an executed theory row.
export function reconcileBatchResults(fixtures, rows, {exitCode, passedCount, failedCount}) {
  const ids = fixtures.map(fixture => fixture.id)
  if (!ids.length || new Set(ids).size !== ids.length) throw new Error('invalid expected fixture identities')
  if (rows.length !== ids.length || new Set(rows.map(row => row.caseId)).size !== rows.length
      || rows.some(row => !ids.includes(row.caseId) || typeof row.passed !== 'boolean')) {
    throw new Error('batch case evidence is missing, duplicated, or unexpected')
  }
  const testCount = passedCount + failedCount
  if (testCount !== ids.length) throw new Error(`batch executed ${testCount} tests; expected ${ids.length}`)
  if (exitCode !== 0 && rows.every(row => row.passed)) throw new Error('test host failed despite passing case evidence')
  if (exitCode === 0 && rows.some(row => !row.passed)) throw new Error('test host succeeded despite failing case evidence')
  const rowPassed = rows.filter(row => row.passed).length
  if (passedCount !== rowPassed || failedCount !== rows.length - rowPassed) {
    throw new Error(`test host outcomes (${passedCount} passed, ${failedCount} failed) differ from case evidence (${rowPassed} passed, ${rows.length - rowPassed} failed); case outcomes are not authoritative after test teardown`)
  }
  return ids.map(id => rows.find(row => row.caseId === id))
}

export function executeConformanceBatch({moduleId, fixtures, command, root, execute}) {
  const directory = mkdtempSync(join(tmpdir(), 'harborline-conformance-'))
  const input = join(directory, 'fixtures.json')
  const output = join(directory, 'results.jsonl')
  try {
    writeFileSync(input, JSON.stringify({moduleId, cases: fixtures}))
    const run = execute(command, root, {
      HARBORLINE_CONFORMANCE_FIXTURE: '',
      HARBORLINE_CONFORMANCE_BATCH: input,
      HARBORLINE_CONFORMANCE_RESULTS: output,
    })
    const passed = Number(/Passed:\s+(\d+)/.exec(run.output)?.[1] ?? 0)
    const failed = Number(/Failed:\s+(\d+)/.exec(run.output)?.[1] ?? 0)
    const skipped = Number(/Skipped:\s+(\d+)/.exec(run.output)?.[1] ?? 0)
    if (skipped) throw new Error('conformance batch skipped tests')
    if (!existsSync(output)) throw new Error(`${moduleId}: no executed-case evidence\n${run.output.split('\n').slice(-60).join('\n')}`)
    const rows = readFileSync(output, 'utf8').trim().split('\n').map(line => JSON.parse(line))
    try {
      return {run, rows: reconcileBatchResults(fixtures, rows, {exitCode: run.exitCode, passedCount: passed, failedCount: failed})}
    } catch (error) {
      throw new Error(`${moduleId}: ${error.message}\n${run.output.split('\n').slice(-60).join('\n')}`, {cause: error})
    }
  } finally {
    rmSync(directory, {recursive: true, force: true})
  }
}
