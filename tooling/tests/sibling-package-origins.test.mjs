import assert from 'node:assert/strict'
import {mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, join, resolve} from 'node:path'
import test from 'node:test'

import {requiredStepIds} from '../gate-contract.mjs'
import {scanSiblingPackageOrigins} from '../gates/scan-sibling-package-origins.mjs'

const repositoryRoot = resolve(import.meta.dirname, '../..')

test('sibling package origin scanner refuses a local bare version and registry pnpm resolution', () => {
  const root = mkdtempSync(join(tmpdir(), 'sibling-package-origin-test-'))
  const write = (file, body) => {
    const path = join(root, file)
    mkdirSync(dirname(path), {recursive: true})
    writeFileSync(path, body)
  }
  try {
    write('engine/package.json', JSON.stringify({name: '@harborline-software/rule-engine'}))
    write('authoring/package.json', JSON.stringify({dependencies: {'@harborline-software/rule-engine': '0.1.0-alpha.0'}}))
    write('authoring/pnpm-lock.yaml', `packages:\n  '@harborline-software/rule-engine@0.1.0-alpha.0':\n    resolution: {tarball: https://registry.npmjs.org/example.tgz}\n`)
    const findings = scanSiblingPackageOrigins(root).findings
    assert.equal(findings.filter(finding => finding.kind === 'bare-local-dependency').length, 1)
    assert.equal(findings.filter(finding => finding.kind === 'registry-lock-resolution').length, 1)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
})

test('sibling package origin scanner accepts a local file dependency and directory lock resolution', () => {
  const root = mkdtempSync(join(tmpdir(), 'sibling-package-origin-test-'))
  const write = (file, body) => {
    const path = join(root, file)
    mkdirSync(dirname(path), {recursive: true})
    writeFileSync(path, body)
  }
  try {
    write('engine/package.json', JSON.stringify({name: '@harborline-software/rule-engine'}))
    write('authoring/package.json', JSON.stringify({dependencies: {'@harborline-software/rule-engine': 'file:../engine'}}))
    write('authoring/pnpm-lock.yaml', `packages:\n  '@harborline-software/rule-engine@file:../engine':\n    resolution: {directory: ../engine, type: directory}\n`)
    assert.deepEqual(scanSiblingPackageOrigins(root).findings, [])
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
})

test('sibling package origin scanner is a required phase-4 gate step after its self-tests', () => {
  const gate = readFileSync(join(repositoryRoot, 'tooling/run-phase-4-gate.mjs'), 'utf8')
  assert.ok(requiredStepIds.includes('sibling-package-origins'))
  assert.ok(requiredStepIds.indexOf('sibling-package-origins') > requiredStepIds.indexOf('tooling-selftests'))
  assert.match(gate, /run\('sibling-package-origins', process\.execPath, \['tooling\/gates\/scan-sibling-package-origins\.mjs', '--json'\], root, true\)/)
})
