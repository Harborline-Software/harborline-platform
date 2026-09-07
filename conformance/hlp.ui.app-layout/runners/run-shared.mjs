import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'
process.exitCode = runUiModuleShared('hlp.ui.app-layout') ? 0 : 1
