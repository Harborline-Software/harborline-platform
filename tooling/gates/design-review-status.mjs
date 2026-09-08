// Keep the runner's aggregation independent of the render parser: phase 4 imports this before
// root-clean-install has installed TypeScript in a fresh clone.
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'

export const EXPIRED_RULE = 'An EXPIRED verdict on a module counted by the catalogue fails the gate on every host.'

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

function backlogState(backlog, now) {
  return {
    listed: new Set(backlog?.modules ?? []),
    active: backlog !== null && now < new Date(`${backlog.deadline}T00:00:00.000Z`),
  }
}

// This is the one dated-backlog decision. Gate rows call it before their parent roll-up and the
// report summary calls it over the emitted rows, so neither surface can acquire its own exception.
// Accepting either a module id or a path keeps the comparison identical for callers that discover
// modules through POSIX paths and callers that discover them through Windows paths.
export function designReviewDecision(moduleIdOrPath, verdict, {backlog = loadExpiredBacklog(), now = new Date()} = {}) {
  const moduleId = moduleIdOf(moduleIdOrPath)
  const expired = /\bEXPIRED\b/.test(verdict.note ?? '')
  const {listed, active} = backlogState(backlog, now)
  const backlogged = expired && active && listed.has(moduleId)
  return {
    status: expired ? (backlogged ? 'PASS' : 'FAIL') : verdict.status,
    note: backlogged ? `EXPIRED (backlog ${backlog.ticket} until ${backlog.deadline})` : verdict.note,
    expired,
    backlogged,
    moduleId,
  }
}

// The sweep supplies exactly the UI modules counted by catalog/modules.yaml. Their lifecycle
// status does not waive an expired human verdict; other unfinished gates retain their worklist role.
// Ticket 083's rule: "a permanently red gate is one people route around". Ticket 334 owns this
// dated exception: listed verdicts become passing notes. Cleared entries must leave the list. The
// exception ends at 00:00 UTC on the deadline on every host.
export function designReviewSummary(modules, {backlog = loadExpiredBacklog(), now = new Date()} = {}) {
  const decisions = modules.map(module => designReviewDecision(module.moduleId,
    module.gates.find(row => row.id === 'assertDesignReview') ?? {status: 'UNBUILT', note: ''},
    {backlog, now}))
  const expired = decisions.filter(decision => decision.expired).map(decision => decision.moduleId)
  const {listed, active} = backlogState(backlog, now)
  const failingExpired = decisions.filter(decision => decision.expired && decision.status === 'FAIL')
    .map(decision => decision.moduleId)
  const staleBacklog = [...listed].filter(moduleId => !expired.includes(moduleId))
  return {
    status: failingExpired.length || staleBacklog.length ? 'FAIL' : 'PASS',
    expired,
    failingExpired,
    staleBacklog,
    rule: EXPIRED_RULE,
    backlog: {count: listed.size, deadline: backlog?.deadline ?? null, ticket: backlog?.ticket ?? null, active},
  }
}

export function designReviewMessage(summary) {
  return `${summary.rule} EXPIRED: ${summary.expired.join(', ') || 'none'}\n`
    + `Design-review backlog: ${summary.backlog.count}; deadline: ${summary.backlog.deadline ?? 'none'}; ticket: ${summary.backlog.ticket ?? 'none'}`
    + (summary.staleBacklog.length ? `; backlog entries not expired (remove): ${summary.staleBacklog.join(', ')}` : '')
}
