#!/usr/bin/env node
// A committed docs/evidence/phase-4/gate.json must name a run that every clone of main can produce.
//
// T-568 committed evidence recording subject.baseHead = 28a17f71, its own lane branch's HEAD. The
// squash merge discarded that commit, so `git branch -r --contains` finds it on nothing: only a
// clone that happens to have kept the loose object can produce it. tooling/release-receipt.mjs
// refuses such evidence, correctly and unchanged (release-receipt-evidence-run-not-in-repository),
// but it refuses at GATE time -- an hour in, on whichever runner's reused clone no longer holds the
// object. macpro's held it and mac16's did not, so every platform merge group the queue routed to
// mac16 failed, weeks after the commit that caused it (T-674). Refusing late is what turned one bad
// commit into an intermittent queue failure; this refuses at the commit that writes it.
//
// design-review provenance already learned this, in its own first line: "New judgements bind staged
// blobs, not a branch commit which a squash merge can discard." Same rule, same reason, applied to
// the phase-4 evidence -- and see checkBase in gates/design-review-provenance.mjs, which is this
// assertion for that record.
//
// The rule: the recorded run is a clean run at a commit main already holds.
//
//   subject.baseHead    an ancestor of main. Anything else dies with the lane branch.
//   subject.testedTree  that commit's own tree, which is the only tree a main commit makes durable.
//                       A dirty-index run names a tree that hangs off no commit, so it is lost the
//                       same way baseHead is, and release-receipt refuses it on the same line.
import {execFileSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'

export const EVIDENCE_PATH = 'docs/evidence/phase-4/gate.json'
const OBJECT_ID = /^[0-9a-f]{40}$/

const git = (root, ...args) => execFileSync('git', args, {
  cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
}).trim()
const succeeds = (root, ...args) => { try { git(root, ...args); return true } catch { return false } }

/** The ref carrying main's history, or null when this clone has not fetched it. */
function mainRef(root) {
  return ['refs/remotes/origin/main', 'refs/heads/main']
    .find(ref => succeeds(root, 'rev-parse', '--verify', '--quiet', ref)) ?? null
}

/** `{field, reason}` naming the field that refused, or null when the recorded run is durable. */
export function checkEvidenceProvenance(root, bytes) {
  let recorded
  try { recorded = JSON.parse(bytes) } catch { return {field: 'subject', reason: 'the evidence is not readable JSON'} }
  const {baseHead, testedTree} = recorded?.subject ?? {}
  const main = mainRef(root)
  // Fail closed. A clone with no main history cannot judge this, and "cannot judge" is never "pass".
  if (!main) return {field: 'subject.baseHead', reason: 'this clone holds no main history to check against; fetch main'}
  if (!OBJECT_ID.test(baseHead ?? '')) return {field: 'subject.baseHead', reason: `not an object id: ${baseHead}`}
  if (!succeeds(root, 'merge-base', '--is-ancestor', baseHead, main)) {
    return {
      field: 'subject.baseHead',
      reason: `${baseHead} is not an ancestor of ${main}. A squash merge discards a lane branch's commits,`
        + ' so evidence recorded on a branch names an object main can never produce. Re-run the gate on a'
        + ' clean checkout of a commit main already holds and commit that report.',
    }
  }
  if (testedTree !== git(root, 'rev-parse', `${baseHead}^{tree}`)) {
    return {
      field: 'subject.testedTree',
      reason: `${testedTree} is not the tree of ${baseHead}. The recorded run must be a clean run:`
        + ' a tree written from a dirty index hangs off no commit and is lost with the branch.',
    }
  }
  return null
}

if (import.meta.main) {
  const root = resolve(process.argv[2] ?? '.')
  let bytes
  // No recorded evidence is nothing to bind. Its absence is validate-repository.mjs's business.
  try { bytes = readFileSync(resolve(root, EVIDENCE_PATH), 'utf8') } catch { process.exit(0) }
  const refusal = checkEvidenceProvenance(root, bytes)
  if (refusal) {
    process.stderr.write(`${EVIDENCE_PATH}: ${refusal.field} - ${refusal.reason}\n`)
    process.exitCode = 1
  }
}
