import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'

process.exitCode = runUiModuleShared('hlp.ui.gantt') ? 0 : 1
