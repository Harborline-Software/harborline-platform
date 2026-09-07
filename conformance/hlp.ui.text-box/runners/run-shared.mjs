import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'

process.exitCode = runUiModuleShared('hlp.ui.text-box') ? 0 : 1
