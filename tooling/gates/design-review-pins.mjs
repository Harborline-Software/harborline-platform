// Ticket 138 slice 6: what a LEGACY design-review record was actually recorded against.
//
// The 59 records on disk bind `reference.revision` -- one sha256 over two files -- and no surface.
// Slice 3 made the surface reach the projection sources and stories; slice 6 has to move those
// records onto it. The only migration that means anything computes each record's surface AT THE
// COMMIT THE VERDICT WAS GIVEN AGAINST. Migrating against today's tree would write down today's
// digests and every one of the 59 would read PASS -- a green wall that hides every change made
// since, which is precisely the blindness the ticket opened with.
//
// The pin is not guessed from the timestamp alone, and it is not the commit that later carried the
// state onto main. It is DERIVED and then VERIFIED:
//
//   the newest commit reachable from the migration ref, dated at or before `recordedAt`, whose
//   gallery/scenarios/<id>.json and specs/modules/ui/<id>/style.css hash to exactly the
//   `reference.revision` the record carries.
//
// The date bound is load-bearing and is what separates this from the obvious version. The five
// modules ticket 138 names were reviewed at 2026-08-26 ~15:30-16:50 EDT and then CHANGED, in
// projection source and stories only, by e4c44eca at 22:54 the same day -- a commit that moves
// neither of the two hashed files. So the recorded revision still matches at e4c44eca and at every
// commit after it: without the date bound the resolver would land on a tree that already contains
// the change, and all five would migrate onto a surface that stands. With it they land on cc9eb48e,
// the pin the ticket verifies at, and they expire naming the file that moved.
//
// The revision check is what keeps the date bound honest in the other direction: a commit that is
// merely old enough is not accepted, only one whose reviewed files are byte-for-byte the ones the
// verdict names. If no commit satisfies both, the record is REFUSED rather than migrated against a
// tree nobody reviewed.

import {execFileSync} from 'node:child_process'
import {createHash} from 'node:crypto'

const git = (platformRoot, ...args) =>
  execFileSync('git', args, {cwd: platformRoot, maxBuffer: 1 << 28, stdio: ['ignore', 'pipe', 'pipe']})

// The two files the legacy revision was computed over, in the order recordVerdict fed them, so the
// digest reproduced here is the same number the record carries. A path missing at that commit hashes
// as empty -- exactly what referenceRevision does for a missing file today.
export function revisionAt(platformRoot, commit, moduleId) {
  const digest = createHash('sha256')
  for (const path of [`gallery/scenarios/${moduleId}.json`, `specs/modules/ui/${moduleId}/style.css`]) {
    let bytes = Buffer.alloc(0)
    try {
      bytes = git(platformRoot, 'show', `${commit}:${path}`)
    } catch {
      bytes = Buffer.alloc(0)
    }
    digest.update(bytes)
  }
  return digest.digest('hex')
}

// Only commits that TOUCH one of the two files can change the revision, so those are the only
// candidates worth hashing. --full-history, not --first-parent: the verdicts were recorded while the
// polish work sat on a topic branch, and the tree the reviewer saw is that branch's commit, not the
// merge that brought it to main days later.
function candidates(platformRoot, moduleId, ref) {
  return git(platformRoot, 'log', '--full-history', '--format=%H %cI', ref, '--',
    `gallery/scenarios/${moduleId}.json`, `specs/modules/ui/${moduleId}/style.css`)
    .toString('utf8').trim().split('\n').filter(Boolean).map(line => line.split(' '))
}

export function resolvePin(platformRoot, record, ref = 'origin/main') {
  const recorded = Date.parse(record.recordedAt)
  if (Number.isNaN(recorded)) throw new Error(`${record.moduleId}: recordedAt "${record.recordedAt}" is not a date`)
  const revision = record.reference?.revision
  if (!revision) throw new Error(`${record.moduleId}: record carries no reference.revision to resolve against`)
  const hit = candidates(platformRoot, record.moduleId, ref)
    .filter(([, date]) => Date.parse(date) <= recorded)
    .find(([commit]) => revisionAt(platformRoot, commit, record.moduleId) === revision)
  if (!hit) {
    throw new Error(`${record.moduleId}: no commit at or before ${record.recordedAt} carries the reviewed revision ${revision.slice(0, 12)}`)
  }
  return {commit: hit[0], committedAt: hit[1]}
}
