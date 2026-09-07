import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
const browser = spawnSync(process.execPath, ['--test', fileURLToPath(new URL('../divider-browser.test.mjs', import.meta.url))], { stdio: ['ignore', 'pipe', 'inherit'], encoding: 'utf8' })
// The gate parses this runner's stdout as ONE JSON report, so the browser test's reporter goes to stderr.
if (browser.stdout) process.stderr.write(browser.stdout)
process.exitCode = runUiModuleShared('hlp.ui.app-shell') && browser.status === 0 ? 0 : 1
