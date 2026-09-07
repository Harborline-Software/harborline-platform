import {runUiModuleShared} from '../../../tooling/run-ui-module-shared.mjs'
process.exitCode = runUiModuleShared('hlp.ui.text-area') ? 0 : 1