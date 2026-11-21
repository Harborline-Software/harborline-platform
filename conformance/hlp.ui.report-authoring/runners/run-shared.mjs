#!/usr/bin/env node
import {runUiModuleShared} from '../../../tooling/run-ui-module-shared.mjs'
process.exitCode = runUiModuleShared('hlp.ui.report-authoring') ? 0 : 1
