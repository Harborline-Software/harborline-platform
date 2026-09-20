import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { delimiter, join, resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import test from 'node:test'

const root = resolve(import.meta.dirname, '../..')
const runner = resolve(root, 'tooling/run-native.mjs')
const fixture = resolve(import.meta.dirname, 'fixtures/run-native-build-process-fixture.mjs')
const resultIds = [
  'forms-typescript-build',
  'rule-runtime-typescript-build',
  'react-typecheck',
  'select-field-typecheck',
  'react-build',
  'forms-typescript-typecheck',
  'rule-runtime-typescript-typecheck',
  'rule-authoring-typescript-typecheck',
  'rule-authoring-typescript-build',
  'copilot-typescript-build',
  'dotnet-build',
]

function runBuild({ invalidTypecheck = false } = {}) {
  const scratch = mkdtempSync(join(tmpdir(), 'run-native-build-ordering-'))
  const shim = join(scratch, 'bin')
  const log = join(scratch, 'events.log')
  for (const [command, entry] of [
    ['npm', join('node_modules', 'npm', 'bin', 'npm-cli.js')],
    ['pnpm', join('node_modules', 'pnpm', 'bin', 'pnpm.cjs')],
  ]) {
    mkdirSync(join(shim, entry, '..'), { recursive: true })
    writeFileSync(join(shim, `${command}.cmd`), '')
    writeFileSync(join(shim, entry), '')
  }
  writeFileSync(log, '')

  const run = spawnSync(process.execPath, ['--import', pathToFileURL(fixture).href, runner, '--build'], {
    cwd: root,
    encoding: 'utf8',
    env: {
      ...process.env,
      PATH: `${shim}${delimiter}${process.env.PATH ?? ''}`,
      DOTNET_HOST_PATH: 'fixture-dotnet',
      HARBORLINE_RUN_NATIVE_FIXTURE_LOG: log,
      ...(invalidTypecheck ? { HARBORLINE_RUN_NATIVE_INVALID_TYPECHECK: '1' } : {}),
    },
  })
  const events = readFileSync(log, 'utf8').trim().split('\n').filter(Boolean)
  rmSync(scratch, { recursive: true, force: true })
  return { run, events }
}

test('run-native build completes React declarations before SelectField typechecking', () => {
  const { run, events } = runBuild()

  assert.equal(run.status, 0, run.stderr || run.stdout)
  const report = JSON.parse(run.stdout)
  assert.equal(report.status, 'PASS')
  assert.equal(report.mode, 'build')
  assert.deepEqual(report.results.map(result => result.id), resultIds)
  assert.ok(events.indexOf('react-build:complete') < events.indexOf('select-field-typecheck:start'), events.join('\n'))
})

test('run-native build fails closed when SelectField typechecking is invalid', () => {
  const { run } = runBuild({ invalidTypecheck: true })

  assert.equal(run.status, 1, run.stderr || run.stdout)
  const report = JSON.parse(run.stdout)
  assert.equal(report.status, 'FAIL')
  const typecheck = report.results.find(result => result.id === 'select-field-typecheck')
  assert.equal(typecheck.passed, false)
  assert.equal(typecheck.exitCode, 2)
  assert.deepEqual(typecheck.command.slice(-2), ['-p', 'tsconfig.typecheck.json'])
})
