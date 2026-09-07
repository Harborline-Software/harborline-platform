import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'
process.exitCode = runUiModuleShared('hlp.ui.select-field') ? 0 : 1
