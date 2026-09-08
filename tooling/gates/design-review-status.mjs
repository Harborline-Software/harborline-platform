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

// The sweep supplies exactly the UI modules counted by catalog/modules.yaml. Their lifecycle
// status does not waive an expired human verdict; other unfinished gates retain their worklist role.
// Ticket 083's rule: "a permanently red gate is one people route around". Ticket 334 owns this
// dated exception: listed verdicts stay red while their aggregate failure is deferred. Cleared
// entries must leave the list. The exception ends at 00:00 UTC on the deadline on every host.
export function designReviewSummary(modules, {backlog = loadExpiredBacklog(), now = new Date()} = {}) {
  const expired = modules.filter(module => module.gates.some(row => row.id === 'assertDesignReview'
    && row.status === 'FAIL' && /\bEXPIRED\b/.test(row.note))).map(module => module.moduleId)
  const listed = new Set(backlog?.modules ?? [])
  const active = backlog !== null && now < new Date(`${backlog.deadline}T00:00:00.000Z`)
  const failingExpired = expired.filter(moduleId => !active || !listed.has(moduleId))
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
