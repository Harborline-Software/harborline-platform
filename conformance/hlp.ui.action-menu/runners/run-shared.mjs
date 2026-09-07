#!/usr/bin/env node
import {spawnSync} from 'node:child_process'
import {resolve} from 'node:path'

import {runUiModuleShared} from '../../../tooling/run-ui-module-shared.mjs'

const root = resolve(import.meta.dirname, '../../..')
const vitest = resolve(root, 'projections/react/ui/hlp.ui.button/node_modules/vitest/vitest.mjs')
// The Blazor lane's stylesheet conformance, run here because bUnit cannot compute a style. Its
// reporter is captured, never written to stdout: the gate parses exactly one JSON document from
// this runner.
const blazorStyle = spawnSync(process.execPath, [
  vitest,
  'run',
  'conformance/hlp.ui.action-menu/action-menu-disclosure.test.mjs',
  '--globals',
  '--environment', 'jsdom',
], {
  cwd: root,
  encoding: 'utf8',
  env: {...process.env, CI: '1', NO_COLOR: '1'},
})

if (blazorStyle.status !== 0) {
  throw new Error(`Action Menu Blazor lane style conformance failed
${blazorStyle.stdout}
${blazorStyle.stderr}`)
}

process.exitCode = runUiModuleShared('hlp.ui.action-menu') ? 0 : 1
