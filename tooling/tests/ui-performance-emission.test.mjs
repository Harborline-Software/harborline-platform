import assert from 'node:assert/strict'
import {existsSync, mkdtempSync, readFileSync, rmSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'
import test from 'node:test'

import {writeUiPerformanceProfile} from '../ui-performance-profile.mjs'

const note = 'Captured, not judged. No budget exists yet; ticket 098 defines this gate as capture until explicit budgets are set.'

test('the UI performance profile contains only measured per-module durations', () => {
  const directory = mkdtempSync(resolve(tmpdir(), 'harborline-ui-performance-'))
  const outputPath = resolve(directory, 'evidence', 'ui-performance.json')
  try {
    writeUiPerformanceProfile(outputPath, [{
      moduleId: 'hlp.ui.example',
      status: 'PASS',
      executions: [
        {id: 'react', exitCode: 0, durationMs: 123},
        {id: 'blazor', exitCode: 0, durationMs: 456},
      ],
    }])

    assert.deepEqual(JSON.parse(readFileSync(outputPath, 'utf8')), {
      schemaVersion: 1,
      note,
      modules: {
        'hlp.ui.example': {reactDurationMs: 123, blazorDurationMs: 456},
      },
    })
  } finally {
    rmSync(directory, {recursive: true, force: true})
  }
})

test('failed executions cannot be recorded as a captured performance profile', () => {
  const directory = mkdtempSync(resolve(tmpdir(), 'harborline-ui-performance-'))
  const outputPath = resolve(directory, 'ui-performance.json')
  try {
    assert.throws(() => writeUiPerformanceProfile(outputPath, [{
      moduleId: 'hlp.ui.example',
      status: 'FAIL',
      executions: [{id: 'react', exitCode: 1, durationMs: 123}],
    }]), /cannot emit UI performance profile/)
    assert.equal(existsSync(outputPath), false)
  } finally {
    rmSync(directory, {recursive: true, force: true})
  }
})
