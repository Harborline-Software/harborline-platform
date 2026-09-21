// Keep the runner's aggregation independent of the render parser: phase 4 imports this before
// root-clean-install has installed TypeScript in a fresh clone.
import {createHash} from 'node:crypto'
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'

export const EXPIRED_RULE = 'An EXPIRED verdict on a module counted by the catalogue fails the gate on every host.'

// T-631, the owner's ruling of 2026-09-20 (route 4). The amnesty used to end at 00:00 UTC on
// 2026-09-30, a date derived from nothing except when the amnesty was written; on that instant
// every platform pull request would have stopped merging. The date is withdrawn and the obligation
// is bound to the thing it was always protecting instead: the 51 reviews are owed before R1 is
// released publicly (T-677), and tooling/release-receipt.mjs refuses the release that does not
// carry them. Nothing here reads a clock any more.
//
// What replaces the deadline as the limit on this amnesty is that the LIST cannot move. An amnesty
// with no expiry and an editable membership is an advisory check with better manners, decaying one
// appended line at a time. So the list is content-addressed and the digest lives in this file,
// beside the rule, rather than beside the data it pins: a module whose review expires after the
// ruling is not amnestied and fails immediately, and extending the amnesty means editing the
// checker in a reviewed diff instead of appending to a JSON array. `staleBacklog` already stopped
// the list being padded with modules that are not expired; this stops it being extended with ones
// that are.
// T-485: Chris's recorded SelectField approval removes that entry, narrowing the frozen set to 50.
export const FROZEN_BACKLOG_DIGEST = 'bb36db6a47450509cf8151619362a717cfcbc74256cdd45aa919babaf50c0cb8'

/** sha256 over the sorted module ids, newline-joined. Order and duplicates cannot change it. */
export function backlogDigest(modules) {
  return createHash('sha256').update([...new Set(modules)].sort().join('\n')).digest('hex')
}

export function loadExpiredBacklog(platformRoot = resolve(import.meta.dirname, '../..')) {
  try {
    return JSON.parse(readFileSync(resolve(platformRoot, 'docs/evidence/design-review/expired-backlog.json'), 'utf8'))
  } catch (error) {
    if (error.code === 'ENOENT') return null
    throw error
  }
}

function moduleIdOf(moduleIdOrPath) {
  const normalized = String(moduleIdOrPath).replaceAll('\\', '/')
  return normalized.split('/').find(part => /^hlp\.ui\.[^/]+$/.test(part)) ?? normalized
}

const listedIn = backlog => new Set(backlog?.modules ?? [])

// This is the one backlog decision. Gate rows call it before their parent roll-up and the report
// summary calls it over the emitted rows, so neither surface can acquire its own exception.
// Accepting either a module id or a path keeps the comparison identical for callers that discover
// modules through POSIX paths and callers that discover them through Windows paths.
export function designReviewDecision(moduleIdOrPath, verdict, {backlog = loadExpiredBacklog()} = {}) {
  const moduleId = moduleIdOf(moduleIdOrPath)
  const expired = /\bEXPIRED\b/.test(verdict.note ?? '')
  // The amnesty holds while the backlog is present and names the module. There is no second
  // condition: a date here is what made the whole set flip red on a calendar instant.
  const backlogged = expired && listedIn(backlog).has(moduleId)
  return {
    status: expired ? (backlogged ? 'PASS' : 'FAIL') : verdict.status,
    note: backlogged ? `EXPIRED (backlog ${backlog.ticket}; owed before R1 is released publicly)` : verdict.note,
    expired,
    backlogged,
    moduleId,
  }
}

// The sweep supplies exactly the UI modules counted by catalog/modules.yaml. Their lifecycle
// status does not waive an expired human verdict; other unfinished gates retain their worklist role.
// Ticket 083's rule: "a permanently red gate is one people route around". Ticket 334 owns the
// exception: listed verdicts become passing notes. Cleared entries must leave the list, and the
// list itself is frozen (see FROZEN_BACKLOG_DIGEST) so the exception cannot grow.
export function designReviewSummary(modules, {backlog = loadExpiredBacklog(), frozen = FROZEN_BACKLOG_DIGEST} = {}) {
  const decisions = modules.map(module => designReviewDecision(module.moduleId,
    module.gates.find(row => row.id === 'assertDesignReview') ?? {status: 'UNBUILT', note: ''},
    {backlog}))
  const expired = decisions.filter(decision => decision.expired).map(decision => decision.moduleId)
  const listed = listedIn(backlog)
  const failingExpired = decisions.filter(decision => decision.expired && decision.status === 'FAIL')
    .map(decision => decision.moduleId)
  const staleBacklog = [...listed].filter(moduleId => !expired.includes(moduleId))
  const digest = backlogDigest(listed)
  const unfrozenBacklog = backlog !== null && digest !== frozen
  return {
    status: failingExpired.length || staleBacklog.length || unfrozenBacklog ? 'FAIL' : 'PASS',
    expired,
    failingExpired,
    staleBacklog,
    unfrozenBacklog,
    rule: EXPIRED_RULE,
    backlog: {count: listed.size, ticket: backlog?.ticket ?? null, digest, frozen},
  }
}

export function designReviewMessage(summary) {
  return `${summary.rule} EXPIRED: ${summary.expired.join(', ') || 'none'}\n`
    + `Design-review backlog: ${summary.backlog.count}; ticket: ${summary.backlog.ticket ?? 'none'}`
    + (summary.staleBacklog.length ? `; backlog entries not expired (remove): ${summary.staleBacklog.join(', ')}` : '')
    + (summary.unfrozenBacklog
      ? `; the backlog is FROZEN and its membership changed: ${summary.backlog.digest} is not ${summary.backlog.frozen}.`
        + ' A review that expires after T-631 is not amnestied. To change the amnesty at all, change'
        + ' FROZEN_BACKLOG_DIGEST in tooling/gates/design-review-status.mjs in the same diff and say why.'
      : '')
}
