// The recorder took `recordedAt` verbatim, so omitting --date wrote `undefined`, which
// JSON.stringify drops. The resulting record was then rejected by this module's OWN reader with
// "design-review record names no reviewer or no date" -- and the CLI had exited 0. Fifty-seven
// verdicts were recorded that way on 2026-08-26, including thirty approvals, and every one read as
// a FAIL that looked like a review problem rather than a tooling one.
import assert from 'node:assert/strict'
import test from 'node:test'
import {mkdirSync, mkdtempSync, readFileSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'

import {recordVerdict, referenceRevision, referenceSurface, reviewVerdict, surfaceFiles} from '../gates/design-review.mjs'

const platformRoot = resolve(import.meta.dirname, '../..')
const scratch = () => mkdtempSync(resolve(tmpdir(), 'design-review-'))

test('a verdict recorded without a date still carries one', () => {
  const root = scratch()
  const record = recordVerdict({
    platformRoot, moduleId: 'hlp.ui.badge', reviewer: 'A Person', verdict: 'approved', root,
  })
  assert.ok(record.recordedAt, 'the record must carry a date')
  assert.ok(!Number.isNaN(Date.parse(record.recordedAt)))
  const written = JSON.parse(readFileSync(resolve(root, 'hlp.ui.badge.json'), 'utf8'))
  assert.equal(written.recordedAt, record.recordedAt, 'the date must survive serialization')
})

// The canary that would have caught it: what is written must satisfy what reads it.
test('an approval that is written reads back as PASS', () => {
  const root = scratch()
  const record = recordVerdict({
    platformRoot, moduleId: 'hlp.ui.badge', reviewer: 'A Person', verdict: 'approved', root,
  })
  const [status] = reviewVerdict({
    record,
    revision: record.reference.revision,
    surface: referenceSurface(platformRoot, 'hlp.ui.badge'),
  })
  assert.equal(status, 'PASS', 'a freshly written approval must not read as FAIL')
})

test('a non-date is refused rather than written', () => {
  assert.throws(() => recordVerdict({
    platformRoot, moduleId: 'hlp.ui.badge', reviewer: 'A Person', verdict: 'approved',
    recordedAt: 'whenever', root: scratch(),
  }), /not a date/)
})

// Unchanged guarantees, kept under test because the write path now has more branches.
test('an automated reviewer is still refused', () => {
  assert.throws(() => recordVerdict({
    platformRoot, moduleId: 'hlp.ui.badge', reviewer: 'claude', verdict: 'approved', root: scratch(),
  }), /named human reviewer/)
})

test('an unrecognised verdict is still refused', () => {
  assert.throws(() => recordVerdict({
    platformRoot, moduleId: 'hlp.ui.badge', reviewer: 'A Person', verdict: 'looks-fine', root: scratch(),
  }), /unrecognised verdict/)
})

// Ticket 138: a verdict binds the SURFACE it approved, file by file, and the gate step refuses a
// verdict whose bound files moved -- naming the one that moved. Run against a fabricated platform
// root so the files can actually be edited; a test that only compares hash strings would pass with
// referenceSurface reading nothing at all.
const fakePlatform = () => {
  const root = mkdtempSync(resolve(tmpdir(), 'design-surface-'))
  mkdirSync(resolve(root, 'specs/modules/ui/hlp.ui.thing'), {recursive: true})
  mkdirSync(resolve(root, 'gallery/scenarios'), {recursive: true})
  writeFileSync(resolve(root, 'specs/modules/ui/hlp.ui.thing/scenarios.json'), '{"scenarios":[]}\n')
  writeFileSync(resolve(root, 'specs/modules/ui/hlp.ui.thing/style.css'), '.thing{color:red}\n')
  writeFileSync(resolve(root, 'gallery/scenarios/hlp.ui.thing.json'), '{"stories":[]}\n')
  return root
}

test('an approval binds every file of the approved surface', () => {
  const platform = fakePlatform()
  const record = recordVerdict({
    platformRoot: platform, moduleId: 'hlp.ui.thing', reviewer: 'A Person', verdict: 'approved', root: scratch(),
  })
  assert.deepEqual(Object.keys(record.reference.surface), surfaceFiles(platform, 'hlp.ui.thing'))
  const [status] = reviewVerdict({record, revision: record.reference.revision, surface: referenceSurface(platform, 'hlp.ui.thing')})
  assert.equal(status, 'PASS')
})

test('changing one approved file expires the verdict and names that file', () => {
  const platform = fakePlatform()
  const record = recordVerdict({
    platformRoot: platform, moduleId: 'hlp.ui.thing', reviewer: 'A Person', verdict: 'approved', root: scratch(),
  })
  writeFileSync(resolve(platform, 'specs/modules/ui/hlp.ui.thing/scenarios.json'), '{"scenarios":[{"id":"new"}]}\n')
  const [status, note] = reviewVerdict({
    record,
    revision: referenceRevision(platform, 'hlp.ui.thing'),
    surface: referenceSurface(platform, 'hlp.ui.thing'),
  })
  assert.equal(status, 'FAIL', 'the spec authority moved; the verdict must not still read as current')
  assert.match(note, /scenarios\.json/)
  assert.doesNotMatch(note, /style\.css/, 'only the file that moved may be named')
})

// Ticket 138 slice 6: the legacy path is CLOSED. A record that binds no surface is refused even
// when its two-file digest still matches, because that digest is exactly what could not see a
// change to a .tsx, a .razor or a story -- the blindness the ticket opened with. Every record on
// disk was migrated onto the surface of the commit its verdict was given against, so an unbound
// record now means a hand-written or pre-migration one, and re-review is the only way forward.
test('a record with no bound surface is refused, whatever its revision says', () => {
  const legacy = {schemaVersion: 1, verdict: 'approved', reviewer: 'A Person', recordedAt: '2026-08-28', reference: {revision: 'c'.repeat(64)}}
  const [status, note] = reviewVerdict({record: legacy, revision: 'c'.repeat(64), surface: {}})
  assert.equal(status, 'FAIL', 'a matching legacy digest must no longer read as a current verdict')
  assert.match(note, /binds no surface/)
  assert.equal(reviewVerdict({record: legacy, revision: 'd'.repeat(64), surface: {}})[0], 'FAIL')
})

// Review 1 of ticket 138, B1: an empty or partial surface used to read PASS unconditionally AND to
// skip the legacy revision check, so an incomplete binding was strictly weaker than no binding.
test('a record binding an empty surface is refused, not passed', () => {
  const platform = fakePlatform()
  const record = {
    schemaVersion: 1, verdict: 'approved', reviewer: 'A Person', recordedAt: '2026-08-28',
    reference: {revision: 'c'.repeat(64), surface: {}},
  }
  const [status, note] = reviewVerdict({record, revision: 'd'.repeat(64), surface: referenceSurface(platform, 'hlp.ui.thing')})
  assert.equal(status, 'FAIL', 'an empty surface must not pass, and must not fall back to a weaker rule')
  assert.match(note, /EXPIRED/, 'a record that bound nothing has not seen any of the files it is judged against')
  assert.match(note, /style\.css/, 'and the refusal names them')
})

test('a record binding only part of the surface is refused naming the missing file', () => {
  const platform = fakePlatform()
  const full = referenceSurface(platform, 'hlp.ui.thing')
  const partial = {'specs/modules/ui/hlp.ui.thing/scenarios.json': full['specs/modules/ui/hlp.ui.thing/scenarios.json']}
  const record = {
    schemaVersion: 1, verdict: 'approved', reviewer: 'A Person', recordedAt: '2026-08-28',
    reference: {revision: referenceRevision(platform, 'hlp.ui.thing'), surface: partial},
  }
  const [status, note] = reviewVerdict({record, revision: referenceRevision(platform, 'hlp.ui.thing'), surface: full})
  assert.equal(status, 'FAIL')
  assert.match(note, /style\.css/, 'the refusal must name the file the record failed to bind')
})
