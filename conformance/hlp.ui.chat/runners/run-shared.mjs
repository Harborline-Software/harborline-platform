import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'

process.exitCode = runUiModuleShared('hlp.ui.chat') ? 0 : 1
