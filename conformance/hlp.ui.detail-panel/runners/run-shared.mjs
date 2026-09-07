import { spawnSync } from 'node:child_process'
import { resolve } from 'node:path'

import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'

const root = resolve(import.meta.dirname, '../../..')
const vitest = resolve(root, 'projections/react/ui/hlp.ui.button/node_modules/vitest/vitest.mjs')
// The Blazor lane's keyboard JavaScript, run here because bUnit cannot execute it. Its reporter is
// captured, never written to stdout: the gate parses exactly one JSON document from this runner.
const blazorJavaScript = spawnSync(process.execPath, [
  vitest,
  'run',
  'conformance/hlp.ui.detail-panel/detail-panel-tabs.test.mjs',
  '--globals',
  '--environment', 'jsdom',
], {
  cwd: root,
  encoding: 'utf8',
  env: {...process.env, CI: '1', NO_COLOR: '1'},
})

if (blazorJavaScript.status !== 0) {
  throw new Error(`Detail Panel Blazor JavaScript conformance failed\n${blazorJavaScript.stdout}\n${blazorJavaScript.stderr}`)
}

process.exitCode = runUiModuleShared('hlp.ui.detail-panel') ? 0 : 1
