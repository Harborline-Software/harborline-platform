import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'

process.exitCode = runUiModuleShared('hlp.ui.scroll-affordance') ? 0 : 1
