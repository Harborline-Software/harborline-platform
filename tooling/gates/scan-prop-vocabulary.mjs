#!/usr/bin/env node
// ADR: `empty` wins because three modules already used it, the largest existing spelling group.
// ADR: `readOnly` wins because form-view and scheduler already share it across lower/Pascal casing.
// ADR: both names describe behavior without binding the contract to labels, text, content, or templates.
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'

import {byLane} from './derive-state-set.mjs'

const EMPTY_CANDIDATE = /empty|noData/i
const READ_ONLY_CANDIDATE = /^read.?only$/i
const EMPTY_PARITY_MODULES = new Set([
  'hlp.ui.accordion', 'hlp.ui.action-menu', 'hlp.ui.activity-log', 'hlp.ui.aspect-lens', 'hlp.ui.chart', 'hlp.ui.chat', 'hlp.ui.conversation-list',
  'hlp.ui.data-grid', 'hlp.ui.gantt', 'hlp.ui.notification-center', 'hlp.ui.rail-labels',
  'hlp.ui.schema-form', 'hlp.ui.spotlight',
])
const READ_ONLY_PARITY_MODULES = new Set(['hlp.ui.numeric-text-box'])

function kindFor(name) {
  if (EMPTY_CANDIDATE.test(name)) return 'empty-state'
  if (READ_ONLY_CANDIDATE.test(name)) return 'read-only'
  return null
}

function canonicalFor(kind) {
  return kind === 'empty-state' ? 'empty' : 'readOnly'
}

export function inspectVocabulary(moduleId, lanes) {
  const findings = []
  for (const lane of ['react', 'blazor']) {
    for (const name of lanes[lane] ?? []) {
      const kind = kindFor(name)
      if (kind && name !== canonicalFor(kind)) {
        findings.push({moduleId, kind: 'off-vocabulary', lane, property: name, expected: canonicalFor(kind)})
      }
    }
  }
  for (const [vocabulary, modules] of [['empty-state', EMPTY_PARITY_MODULES], ['read-only', READ_ONLY_PARITY_MODULES]]) {
    if (!modules.has(moduleId) || lanes.react === null || lanes.blazor === null) continue
    const react = lanes.react.filter(name => kindFor(name) === vocabulary)
    const blazor = lanes.blazor.filter(name => kindFor(name) === vocabulary)
    if (JSON.stringify(react) !== JSON.stringify(blazor)) {
      findings.push({
        moduleId,
        kind: 'lane-divergence',
        vocabulary,
        react,
        blazor,
        expected: `the same ${vocabulary} property name in both lanes`,
      })
    }
  }
  return findings
}

function discoverModules(modules) {
  return Object.entries(modules)
    .filter(([moduleId, module]) => moduleId.startsWith('hlp.ui.')
      && typeof module.projections?.react?.path === 'string'
      && (typeof module.projections?.blazor?.path === 'string'
        || typeof module.projections?.dotnet?.path === 'string'))
    .map(([moduleId]) => moduleId)
    .sort()
}

function runScan(platformRoot) {
  const catalog = JSON.parse(readFileSync(resolve(platformRoot, 'catalog/modules.yaml'), 'utf8'))
  const moduleIds = discoverModules(catalog.modules ?? {})
  const findings = []
  for (const moduleId of moduleIds) {
    const lanes = byLane(platformRoot, moduleId,
      prop => EMPTY_CANDIDATE.test(prop.name) || READ_ONLY_CANDIDATE.test(prop.name))
    findings.push(...inspectVocabulary(moduleId, lanes))
  }
  return {checked: moduleIds.length, findings}
}

function runCanary() {
  const failures = []
  const assert = (name, predicate) => {
    if (!predicate) failures.push(name)
  }
  const offVocabulary = inspectVocabulary('hlp.ui.numeric-text-box', {react: ['emptyText', 'readonly'], blazor: ['empty', 'readOnly']})
  assert('off-vocabulary spelling is reported', offVocabulary.some(finding => finding.kind === 'off-vocabulary'))
  assert('lane divergence is reported', offVocabulary.some(finding => finding.kind === 'lane-divergence'))
  assert('canonical agreement is accepted', inspectVocabulary('hlp.ui.numeric-text-box', {react: ['empty', 'readOnly'], blazor: ['empty', 'readOnly']}).length === 0)
  assert('hlp.ui module discovery', discoverModules({
    'hlp.ui.canary': {projections: {react: {path: 'react'}, blazor: {path: 'blazor'}}},
    'hlp.kernel.canary': {projections: {react: {path: 'react'}, blazor: {path: 'blazor'}}},
  }).length === 1)
  if (failures.length) {
    process.stderr.write(`canary FAIL:\n${failures.map(failure => `  ${failure}`).join('\n')}\n`)
    return 1
  }
  process.stdout.write('canary OK — off-vocabulary and lane-divergent prop names are refused\n')
  return 0
}

if (import.meta.main) {
  const argv = process.argv.slice(2)
  if (argv.includes('--canary')) {
    process.exitCode = runCanary()
  } else {
    const platformRoot = resolve(argv.find(argument => !argument.startsWith('--'))
      ?? `${import.meta.dirname}/../..`)
    const {checked, findings} = runScan(platformRoot)
    if (argv.includes('--json')) {
      process.stdout.write(`${JSON.stringify({generated: true, checked, findings}, null, 2)}\n`)
    } else {
      process.stdout.write(`${checked} UI module pairs checked; ${findings.length} prop vocabulary finding${findings.length === 1 ? '' : 's'}\n`)
      for (const finding of findings) process.stdout.write(`  ${finding.moduleId}: ${JSON.stringify(finding)}\n`)
    }
    process.exitCode = findings.length > 0 ? 1 : 0
  }
}
