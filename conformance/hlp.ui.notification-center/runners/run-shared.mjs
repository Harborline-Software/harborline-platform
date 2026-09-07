import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'
process.exitCode = runUiModuleShared('hlp.ui.notification-center') ? 0 : 1
