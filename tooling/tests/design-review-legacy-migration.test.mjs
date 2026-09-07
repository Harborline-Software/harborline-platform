// Ticket 138 slice 6. The 59 design-review records on disk bound one sha256 over two files and no
// surface, which is the blindness the ticket opened with: the five modules the 2026-08-26 polish
// pass fixed entirely in projection source and gallery stories still read as current verdicts.
//
// This suite holds the migration to the only property that makes it worth anything: each record was
// bound to the surface OF THE COMMIT ITS VERDICT WAS GIVEN AGAINST, not of the working tree it was
// migrated in. The difference is invisible in the record shape and total in the meaning -- migrating
// against today's tree writes today's digests, every record reads PASS, and every change made since
// the verdict disappears. `the migration is judged at the polish pin` below is the test that sees
// it: at e4c44eca the modules the polish changed must EXPIRE and the ones it did not must STAND.

import assert from 'node:assert/strict'
import test from 'node:test'
import {execFileSync} from 'node:child_process'
import {mkdtempSync, readFileSync, readdirSync, rmSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'

import {createGateModel} from '../gates/gate-rows.mjs'
import {recordsRoot, referenceRevision, referenceSurface, reviewVerdict} from '../gates/design-review.mjs'
import {resolvePin} from '../gates/design-review-pins.mjs'

const platformRoot = resolve(import.meta.dirname, '../..')
const git = (...args) => execFileSync('git', args, {cwd: platformRoot, maxBuffer: 1 << 28, stdio: ['ignore', 'pipe', 'pipe']}).toString('utf8')

// The polish pass ticket 138 was opened over: one commit that both makes the five changes and
// records the verdicts, which is why they never expired.
const POLISH = 'e4c44eca'

// The five modules the ticket names, with the change it describes. `data-grid` is the odd one and is
// not silently dropped: its verdict was recorded on 2026-08-28, AFTER the polish, so its pin already
// contains the scroll-handler fix and no honest migration can expire it on that change. It is
// covered by the "flagged today" test below instead, where it expires on what moved since.
const TICKET_MODULES = ['hlp.ui.context-menu', 'hlp.ui.data-export-button', 'hlp.ui.data-grid', 'hlp.ui.date-field', 'hlp.ui.date-time-field']
const EXPIRED_AT_POLISH = {
  'hlp.ui.context-menu': 'ContextMenu.stories',
  'hlp.ui.data-export-button': 'ShipyardDataExportButton.razor',
  'hlp.ui.date-field': 'DateField.stories.tsx',
  'hlp.ui.date-time-field': 'DateTimeField.stories.tsx',
}

// Ticket 253: the public repository starts a fresh history, so the commits these pins name live only
// in the archive. The two derivation tests below need that history; when the clone carries it (an
// `archive` remote, or the polish commit itself) they run against it, and when it does not they are
// skipped BY NAME rather than failing on a history that was never there. The verdicts themselves
// are judged by surface digests (design-review.mjs) and do not depend on this.
const carriesArchiveHistory = (() => {
  try {
    git('cat-file', '-e', `${POLISH}^{commit}`)
    return true
  } catch {
    return false
  }
})()
const HISTORY_REF = (() => {
  try {
    git('rev-parse', '--verify', '--quiet', 'archive/main')
    return 'archive/main'
  } catch {
    return 'origin/main'
  }
})()
const FRESH_HISTORY = carriesArchiveHistory
  ? false
  : 'this history begins after every verdict (ticket 253, fresh public history); the pins are facts recorded in the archive'

const records = () => readdirSync(recordsRoot).filter(name => name.endsWith('.json')).sort()
  .map(name => JSON.parse(readFileSync(resolve(recordsRoot, name), 'utf8')))

test('every design-review record binds a surface, so none can reach the gate on the legacy digest', () => {
  const all = records()
  assert.ok(all.length >= 59, `expected the recorded corpus, got ${all.length}`)
  for (const record of all) {
    assert.ok(record.reference?.surface, `${record.moduleId} binds no surface`)
    assert.ok(Object.keys(record.reference.surface).length > 0, `${record.moduleId} binds an empty surface`)
    assert.ok(record.reference?.pin, `${record.moduleId} does not name the commit it was migrated against`)
  }
})

// The pin is derived, not written down by hand, so it can be recomputed from the record itself and
// must come back the same. A pin edited to a friendlier commit fails here.
test('every pin is the commit the verdict was actually given against', {skip: FRESH_HISTORY}, () => {
  for (const record of records()) {
    const {commit} = resolvePin(platformRoot, record, HISTORY_REF)
    assert.equal(commit, record.reference.pin, `${record.moduleId} names a pin its own revision and date do not resolve to`)
  }
})

// The load-bearing test. Reviewed AT THE POLISH COMMIT, a correctly migrated corpus splits: the
// modules the polish changed expire and name the file, and the ones it did not still stand.
// Migrate against today's tree instead and every module in both sets stands, because the record
// would carry the digests of the tree it is being compared with.
test('the migration is judged at the polish pin: the changed modules expire and the rest stand', {skip: FRESH_HISTORY}, t => {
  const scratchRoot = mkdtempSync(resolve(tmpdir(), 'design-review-polish-'))
  const scratch = resolve(scratchRoot, 'tree')
  git('worktree', 'add', '--detach', '--quiet', scratch, POLISH)
  t.after(() => {
    git('worktree', 'remove', '--force', scratch)
    rmSync(scratchRoot, {recursive: true, force: true})
  })
  const expired = new Map()
  const standing = []
  for (const record of records()) {
    // A verdict recorded after the polish cannot be judged at it -- its pin is not an ancestor, so
    // the tree here predates the files it bound. Skipped by NAME, not by a silent catch.
    let judgeable = true
    try {
      git('merge-base', '--is-ancestor', record.reference.pin, POLISH)
    } catch {
      judgeable = false
    }
    if (!judgeable) continue
    const [status, note] = reviewVerdict({
      record,
      revision: referenceRevision(scratch, record.moduleId),
      surface: referenceSurface(scratch, record.moduleId),
    })
    if (status === 'PASS') standing.push(record.moduleId)
    else expired.set(record.moduleId, note)
  }
  // Both sets must be non-trivial: an all-expired corpus is a migration against nothing and an
  // all-standing one is a migration against today.
  assert.ok(standing.length > 30, `a migration bound to its pin leaves most verdicts standing at the pin, got ${standing.length}`)
  assert.ok(expired.size > 0, 'the polish changed modules; none of them expiring means the surface is not bound to the pin')
  for (const [moduleId, file] of Object.entries(EXPIRED_AT_POLISH)) {
    const note = expired.get(moduleId)
    assert.ok(note, `${moduleId} was changed by the polish and must EXPIRE at it, but it stands`)
    assert.match(note, /EXPIRED/)
    assert.ok(note.includes(file), `${moduleId} must name ${file} in its refusal, got "${note}"`)
    assert.ok(!standing.includes(moduleId))
  }
  for (const moduleId of standing) {
    assert.ok(!(moduleId in EXPIRED_AT_POLISH))
  }
})

// Acceptance line 4, read against the tree as it is now: all five are off green and each refusal
// names a file, so the flag is actionable rather than "something changed".
test('the five modules ticket 138 names are flagged for re-review today', () => {
  const byId = new Map(records().map(record => [record.moduleId, record]))
  for (const moduleId of TICKET_MODULES) {
    const record = byId.get(moduleId)
    assert.ok(record, `${moduleId} has no design-review record`)
    const [status, note] = reviewVerdict({
      record,
      revision: referenceRevision(platformRoot, moduleId),
      surface: referenceSurface(platformRoot, moduleId),
    })
    assert.equal(status, 'FAIL', `${moduleId} must be flagged for re-review, got ${status}: ${note}`)
    assert.match(note, /EXPIRED/)
    assert.match(note, /\.(razor|tsx|css|json|js|cs|ts)/, `the refusal must name a file, got "${note}"`)
  }
})

// The gate STEP, not the rule underneath it: a legacy-shaped record planted in the context the
// report builds must be refused. This is the row that keeps the migration complete -- a record
// slipped back in without a surface cannot ride the two-file digest to a PASS.
test('the gate step refuses a planted record that binds no surface', () => {
  const model = createGateModel(platformRoot)
  const moduleId = 'hlp.ui.badge'
  const context = model.contextFor(moduleId)
  const legacy = {
    schemaVersion: 1,
    moduleId,
    verdict: 'approved',
    reviewer: 'A Person',
    recordedAt: '2026-08-28T00:00:00.000Z',
    // The digest of the tree it is being judged in: on the old rule this record read PASS.
    reference: {revision: context.reviewRevision, source: `gallery/scenarios/${moduleId}.json`},
  }
  const [status, note] = model.verdictOf('assertDesignReview')({...context, designReview: legacy})
  assert.equal(status, 'FAIL', 'a record whose legacy digest still matches must no longer pass the gate step')
  assert.match(note, /binds no surface/)
  // The control: the record on disk for the same module is bound, so the refusal is about the
  // binding and not about the module.
  assert.ok(context.designReview.reference.surface, `${moduleId} on disk must be migrated`)
})
