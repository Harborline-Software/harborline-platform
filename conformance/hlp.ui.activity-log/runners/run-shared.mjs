#!/usr/bin/env node
import {runUiModuleShared} from '../../../tooling/run-ui-module-shared.mjs'
process.exitCode = runUiModuleShared('hlp.ui.activity-log') ? 0 : 1
