import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'

process.exitCode = runUiModuleShared('hlp.ui.confirm-dialog') ? 0 : 1
