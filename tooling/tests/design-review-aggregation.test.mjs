import assert from 'node:assert/strict'
import test from 'node:test'
import {evaluateStepStdout} from '../gate-step-evidence.mjs'
import {reviewVerdict} from '../gates/design-review.mjs'

test('a planted EXPIRED review on a counted non-terminal module fails the phase-4 aggregation', () => {
  const record = {schemaVersion: 1, reviewer: 'A Person', recordedAt: '2026-08-26',
    verdict: 'approved', reference: {surface: {'Component.razor': 'before'}}}
  const [status, note] = reviewVerdict({record, revision: 'revision', surface: {'Component.razor': 'after'}})
  assert.equal(status, 'FAIL')
  assert.match(note, /EXPIRED/)
  const modules = [{moduleId: 'hlp.ui.accordion', catalogStatus: 'extracted-candidate', terminal: false,
    gates: [{id: 'assertDesignReview', status, note}]}]
  const evaluate = report => evaluateStepStdout({stepId: 'ui-gate-model', json: true, status: 0, stdout: JSON.stringify(report)})
  const result = evaluate({modules})
  assert.equal(result.status, 1, 'an exit-zero static sweep must not hide a counted EXPIRED verdict')
  assert.equal(result.report.status, 'FAIL')
  assert.match(result.failure, /hlp.ui.accordion/)
  assert.match(result.report.designReview.rule, /EXPIRED.*fails.*every host/)
  const [caseStatus, caseNote] = reviewVerdict({record, revision: 'revision', surface: {'component.razor': 'before'}})
  assert.equal(caseStatus, 'FAIL', 'a case-only path mismatch must expire even when the digest is identical')
  assert.match(caseNote, /no longer has Component.razor; has gained component.razor/)
  const caseModules = structuredClone(modules)
  caseModules[0].gates[0] = {id: 'assertDesignReview', status: caseStatus, note: caseNote}
  assert.equal(evaluate({modules: caseModules}).status, 1)
  for (const status of ['PASS', 'UNBUILT', 'NOT-APPLICABLE']) {
    const control = structuredClone(modules)
    control[0].gates[0] = {id: 'assertDesignReview', status, note: 'control'}
    assert.equal(evaluate({modules: control}).status, 0)
  }
})
