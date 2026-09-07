import assert from 'node:assert/strict'
import test from 'node:test'

import {inspectVocabulary} from '../gates/scan-prop-vocabulary.mjs'

test('prop vocabulary accepts only canonical cross-lane agreement', () => {
  assert.deepEqual(inspectVocabulary('hlp.ui.numeric-text-box', {
    react: ['empty', 'readOnly'],
    blazor: ['empty', 'readOnly'],
  }), [])
})

test('prop vocabulary reports off-vocabulary and lane-divergent names', () => {
  const findings = inspectVocabulary('hlp.ui.numeric-text-box', {
    react: ['emptyText', 'readonly'],
    blazor: ['empty', 'readOnly'],
  })
  assert.equal(findings.filter(finding => finding.kind === 'off-vocabulary').length, 2)
  assert.equal(findings.filter(finding => finding.kind === 'lane-divergence').length, 1)
})
