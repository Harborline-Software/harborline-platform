import assert from 'node:assert/strict'
import test from 'node:test'
import {execFileSync} from 'node:child_process'
import {resolve, dirname} from 'node:path'
import {fileURLToPath} from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')

// The MVP is headless, so the browser parity suite is not in the landing path. That is a deliberate
// narrowing and it must stay honest: exactly the browser steps leave the required set, and nothing
// else does. A gate that can quietly drop any step is worse than one that runs a slow suite.
const requiredUnder = headlessValue => JSON.parse(execFileSync(process.execPath, ['-e',
  "import('./tooling/gate-contract.mjs').then(m => console.log(JSON.stringify(m.requiredStepIds)))"],
  {cwd: root, encoding: 'utf8', env: {...process.env, HARBORLINE_GATE_HEADLESS: headlessValue}}))

test('headless drops exactly the browser steps from the required set', () => {
  const full = requiredUnder('')
  const lean = requiredUnder('1')
  assert.deepEqual(full.filter(id => id !== 'gallery-gate'), lean,
    'headless must remove gallery-gate and nothing else')
  assert.ok(full.includes('gallery-gate'), 'the full set still requires the browser suite')
  assert.equal(lean.length, full.length - 1)
})

test('every non-browser step is still required when headless', () => {
  const lean = requiredUnder('1')
  for (const id of ['build', 'native-tests', 'perf-budgets', 'ui-shared-conformance',
    'package-consumers', 'catalog-final', 'tooling-selftests', 'generation-smoke']) {
    assert.ok(lean.includes(id), `${id} must keep blocking a landing when headless`)
  }
})
