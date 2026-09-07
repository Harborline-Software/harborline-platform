import {runUiModuleShared} from '../../../tooling/run-ui-module-shared.mjs'
process.exitCode = runUiModuleShared('hlp.ui.schema-form') ? 0 : 1