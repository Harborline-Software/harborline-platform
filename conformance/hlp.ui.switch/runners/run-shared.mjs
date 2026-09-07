import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'
process.exitCode = runUiModuleShared('hlp.ui.switch') ? 0 : 1
