import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {test} from 'node:test'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

import {resolveCommand, runnerEnvironment} from '../resolve-command.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const reactRoot = resolve(root, 'projections/react/ui/hlp.ui.button')

test('shared conformance runs rule-reevaluation and excludes the wall-clock budget file', () => {
  const command = resolveCommand('npm', [
    'exec', '--', 'vitest', 'run',
    '--config', '../hlp.ui.schema-form/vitest.config.ts',
    '--root', '../hlp.ui.schema-form',
    'src/__tests__/SchemaForm.rule-reevaluation.test.tsx',
    'src/__tests__/SchemaForm.performance.test.tsx',
    '--maxWorkers=1',
  ])
  const result = spawnSync(command.executable, command.args, {
    cwd: reactRoot,
    encoding: 'utf8',
    env: {...process.env, ...runnerEnvironment, HARBORLINE_SHARED_CONFORMANCE: '1'},
  })
  const output = `${result.stdout ?? ''}\n${result.stderr ?? ''}`
  assert.equal(result.status, 0, output)
  assert.match(output, /Test Files\s+1 passed \(1\)/, output)
  assert.match(output, /Tests\s+1 passed \(1\)/, output)
})
