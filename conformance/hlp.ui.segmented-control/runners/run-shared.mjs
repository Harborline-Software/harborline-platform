import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'
process.exitCode = runUiModuleShared('hlp.ui.segmented-control') ? 0 : 1
