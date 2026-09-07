import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {readFileSync, readdirSync} from 'node:fs'
import path from 'node:path'
import test from 'node:test'

import {runnerEnvironment} from '../resolve-command.mjs'

// Ticket 270: on macOS the .NET CLI's telemetry sender stalls `dotnet` for ~21 s on roughly half
// of all spawns (measured on macpro, M1 Pro, 2026-09-05: 4 of 8 identical `dotnet test --no-build`
// runs at 21.5 s against 1.8 s, unchanged by piped/ignored/file-backed stdio; with the opt-out,
// 8 of 8 at 1.1-1.2 s). Every gate runner therefore spawns with the opt-out set, and these tests
// are the control: they are red if the flag is dropped from the shared environment or if a runner
// goes back to building its own inline `{ CI, NO_COLOR }` object.
const toolingRoot = path.resolve(import.meta.dirname, '..')

test('the shared runner environment opts out of .NET CLI telemetry', () => {
  assert.equal(runnerEnvironment.DOTNET_CLI_TELEMETRY_OPTOUT, '1')
  assert.equal(runnerEnvironment.CI, '1')
  assert.equal(runnerEnvironment.NO_COLOR, '1')
})

test('a child spawned with the shared environment receives the opt-out', () => {
  const child = spawnSync(process.execPath, ['-p', 'process.env.DOTNET_CLI_TELEMETRY_OPTOUT'], {
    encoding: 'utf8',
    env: {...process.env, ...runnerEnvironment},
  })
  assert.equal(child.status, 0)
  assert.equal(child.stdout.trim(), '1')
})

test('no tooling runner builds its own spawn environment', () => {
  const offenders = readdirSync(toolingRoot)
    .filter(entry => entry.endsWith('.mjs') && entry !== 'resolve-command.mjs')
    .filter(entry => readFileSync(path.join(toolingRoot, entry), 'utf8').includes("CI: '1', NO_COLOR: '1'"))
  assert.deepEqual(offenders, [], `these runners must spread runnerEnvironment instead: ${offenders.join(', ')}`)
})
