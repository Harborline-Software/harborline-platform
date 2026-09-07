// Ticket 138 slice 4. The ticket asks for the false-positive claim to be DEMONSTRATED rather than
// asserted: a change that cannot alter rendered appearance does not expire a verdict. Every case
// here edits a real file of a real module in a scratch copy of this tree and runs the gate's own
// reviewVerdict over the surface the gate itself computes.
import assert from 'node:assert/strict'
import test from 'node:test'

import {CASES, CASE_MODULE, runCase, scratchCopy} from '../gates/design-surface-cases.mjs'
import {referenceSurface} from '../gates/design-review.mjs'

const neutral = CASES.filter(kase => kase.neutral)
const controls = CASES.filter(kase => !kase.neutral)

test('the proof has a positive control, or it proves nothing', () => {
  assert.ok(controls.length > 0, 'a suite of only-green cases cannot tell "neutral edits pass" from "nothing fails"')
  assert.ok(neutral.length >= 6, 'the ticket names six behaviour-neutral edit classes')
})

for (const kase of neutral) {
  test(`${kase.id}: editing ${kase.file} does NOT expire the verdict`, () => {
    const {before, after, status, note} = runCase(kase)
    assert.deepEqual(after, before, `${kase.id} moved the approved surface: ${note}`)
    assert.equal(status, 'PASS', `${kase.id} expired the verdict it must not: ${note}`)
  })
}

for (const kase of controls) {
  test(`${kase.id}: editing ${kase.file} DOES expire the verdict, naming that file`, () => {
    const {before, after, status, note} = runCase(kase)
    assert.notDeepEqual(after, before, 'an appearance change must move the surface')
    assert.equal(status, 'FAIL')
    assert.match(note, /EXPIRED/)
    assert.match(note, new RegExp(kase.file.split('/').pop().replace('.', '\\.')))
  })
}

// The trap this whole slice can fall into: a scratch copy that omits the file a case edits, or an
// `apply` that no longer matches, would leave every neutral case green for the wrong reason.
// runCase refuses a no-op edit; this proves that refusal is live rather than trusted.
test('a case that edits nothing is refused, not counted as a pass', () => {
  assert.throws(() => runCase({...CASES[0], apply: text => text}), /edited nothing/)
})

test('the scratch copy really carries the module the cases edit', () => {
  const copy = scratchCopy()
  const surface = referenceSurface(copy, CASE_MODULE)
  assert.ok(surface, 'the copy must be a platform root the gate can read a surface from')
  for (const [file, hash] of Object.entries(surface)) {
    assert.notEqual(hash, 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', `${file} hashed as empty; the copy is missing it`)
  }
})
