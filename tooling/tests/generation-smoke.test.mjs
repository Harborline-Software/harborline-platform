import assert from 'node:assert/strict'
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'

import { generateSmokeProject, loadModuleSpec } from '../generate-blazor-smoke.mjs'

function fixture() {
  const root = mkdtempSync(join(tmpdir(), 'harborline-generation-smoke-test-'))
  const moduleRoot = join(root, 'hlp.ui.example')
  mkdirSync(moduleRoot, { recursive: true })
  writeFileSync(join(moduleRoot, 'interface.yaml'), JSON.stringify({ schemaVersion: 1, moduleId: 'hlp.ui.example', invariants: ['renders'] }))
  writeFileSync(join(moduleRoot, 'quality.yaml'), JSON.stringify({ schemaVersion: 1, moduleId: 'hlp.ui.example' }))
  writeFileSync(join(moduleRoot, 'scenarios.json'), JSON.stringify({ schemaVersion: 1, moduleId: 'hlp.ui.example', scenarios: [{ id: 'example.default', name: 'Default', surface: 'default', sourceCaseIds: ['example.defaults'] }] }))
  return { root, moduleRoot }
}

test('generates one Razor smoke surface from spec authority only', () => {
  const { root } = fixture()
  try {
    const outputRoot = join(root, 'out')
    const generated = generateSmokeProject({ specRoot: root, moduleIds: ['hlp.ui.example'], outputRoot })
    assert.deepEqual(generated, [{ moduleId: 'hlp.ui.example', component: 'HlpUiExampleSmoke', scenarios: 1 }])
    assert.match(readFileSync(join(outputRoot, 'HlpUiExampleSmoke.razor'), 'utf8'), /data-gallery-scenario="example.default"/)
  } finally { rmSync(root, { recursive: true, force: true }) }
})

test('fails closed when a required spec artifact is missing', () => {
  const { root, moduleRoot } = fixture()
  try {
    rmSync(join(moduleRoot, 'quality.yaml'))
    assert.throws(() => loadModuleSpec(root, 'hlp.ui.example'), /missing required spec artifact quality.yaml/)
  } finally { rmSync(root, { recursive: true, force: true }) }
})

test('fails closed when scenario structure cannot generate a smoke surface', () => {
  const { root, moduleRoot } = fixture()
  try {
    writeFileSync(join(moduleRoot, 'scenarios.json'), JSON.stringify({ schemaVersion: 1, moduleId: 'hlp.ui.example', scenarios: [] }))
    assert.throws(() => loadModuleSpec(root, 'hlp.ui.example'), /has no scenarios/)
  } finally { rmSync(root, { recursive: true, force: true }) }
})
