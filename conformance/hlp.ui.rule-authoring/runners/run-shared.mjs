#!/usr/bin/env node

import { readFileSync } from 'node:fs'
import { runUiModuleShared } from '../../../tooling/run-ui-module-shared.mjs'

const fixture = JSON.parse(readFileSync(new URL('../fixtures.yaml', import.meta.url), 'utf8'))
const producer = JSON.parse(readFileSync(new URL('../../hlp.blocks.builder-definitions/rules-editor-contract-fixtures.json', import.meta.url), 'utf8'))
const lifecycle = producer.lifecycle.responses
const expectedLifecycle = [
  ['create', 'Draft', 1],
  ['publish', 'Published', 2],
  ['publish-replay', 'Published', 2],
  ['publish-stale', 'definition.revision_conflict', null],
  ['materialize-latest', 'fixture-v1', '1.0.0'],
  ['archive', false, true],
]
const actualLifecycle = lifecycle.map(response => response.operation === 'publish-stale'
  ? [response.operation, response.refusal.code, null]
  : response.operation === 'materialize-latest'
    ? [response.operation, response.materialization.bindings[0].versionId, response.materialization.bindings[0].winningWatermark]
    : response.operation === 'archive'
      ? [response.operation, response.listVisible, response.publishedPinStillResolves]
      : [response.operation, response.status, response.revision])
const exactPreview = producer.preview.cases.every(item => fixture.outcomes.includes(item.expected.kind) && item.expected.ruleName === item.ruleName && item.expected.memberName === item.memberName)
if (fixture.lifecycle.identity !== lifecycle[0].identity.definitionId || JSON.stringify(actualLifecycle) !== JSON.stringify(expectedLifecycle) || !exactPreview || !producer.preview.clockUtc || !producer.preview.label) process.exit(1)
process.exitCode = runUiModuleShared('hlp.ui.rule-authoring') ? 0 : 1
