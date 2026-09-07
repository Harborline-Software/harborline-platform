#!/usr/bin/env node
import {runUiModuleShared} from '../../../tooling/run-ui-module-shared.mjs'
process.exitCode = runUiModuleShared('hlp.ui.loading-state') ? 0 : 1
