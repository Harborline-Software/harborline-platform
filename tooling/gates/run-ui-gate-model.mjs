#!/usr/bin/env node
// Ticket 098 — the static sweep, over every hlp.ui.* module, writing the receipt that makes a
// module's catalog status checkable.
//
// Until this existed, `catalog/modules.yaml`'s module `status` was validated by nothing:
// validate-repository.mjs checks PROJECTION status against an allowed set and never looks at the
// module's own. So every UI module has carried `extracted-candidate` or `gap-implemented` since it
// was written and either could have been changed to anything at all without a check noticing. A
// status that cannot be wrong is not a status; it is a comment.
//
// This step records unfinished work; the shared EXPIRED rule applies ticket 334's dated backlog.
// validate-repository.mjs refuses a status claim the receipt does not support. Other unfinished
// gates remain a worklist rather than making every non-terminal module fail this step.
//
// Usage: node run-ui-gate-model.mjs [<platform-root>] [--json|--canary]

import {mkdirSync, readFileSync, writeFileSync} from 'node:fs'
import {resolve} from 'node:path'

import {createGateModel} from './gate-rows.mjs'
import {designReviewMessage, designReviewSummary, loadExpiredBacklog} from './design-review-status.mjs'

const argv = process.argv.slice(2)
const positional = argv.filter(argument => !argument.startsWith('--'))
// This tool lives inside the repository it measures: the root is two levels up from here.
const platformRoot = resolve(positional[0] ?? `${import.meta.dirname}/../..`)

// A gate a person declared not-applicable counts as earned; every other status does not. PARTIAL is
// deliberately NOT terminal — "declared but not rendered" is the state fifty-three modules are in,
// and admitting it would make the status mean "someone wrote a scenario id".
const TERMINAL_STATUSES = new Set(['PASS', 'NOT-APPLICABLE'])

export function terminalFor(rows) {
  return rows.length > 0 && rows.every(row => TERMINAL_STATUSES.has(row.status))
}

function build() {
  const {TIER1_GATE_IDS, gateRows, contextFor} = createGateModel(platformRoot)
  const catalog = JSON.parse(readFileSync(resolve(platformRoot, 'catalog/modules.yaml'), 'utf8')).modules
  const moduleIds = Object.keys(catalog).filter(id => id.startsWith('hlp.ui.')).sort()

  const tally = {}
  const modules = moduleIds.map(moduleId => {
    const rows = gateRows(contextFor(moduleId)).map(({id, status, note}) => ({id, status, note}))
    for (const row of rows) tally[row.status] = (tally[row.status] ?? 0) + 1
    return {
      moduleId,
      presentation: catalog[moduleId].presentation?.disposition ?? 'unknown',
      catalogStatus: catalog[moduleId].status,
      terminal: terminalFor(rows),
      gates: rows,
    }
  })

  const designReview = designReviewSummary(modules, {backlog: loadExpiredBacklog(platformRoot)})
  return {
    schemaVersion: 1,
    status: designReview.status,
    designReview,
    ticket: '098',
    subject: {moduleCount: modules.length, gateIds: TIER1_GATE_IDS},
    modules,
    tally,
  }
}

// assertGateCanFail, applied to the receipt's own predicate. Goes through terminalFor() — the same
// function build() uses — because a canary that recomputes the rule locally reports a DEAD gate as
// healthy, which is the failure this whole discipline exists to catch.
function canary() {
  const {TIER1_GATE_IDS, declaredDisposition} = createGateModel(platformRoot)
  const failures = []
  const green = TIER1_GATE_IDS.map(id => ({id, status: 'PASS', note: ''}))

  if (!terminalFor(green)) failures.push('an all-PASS row must be terminal')
  if (!terminalFor(green.map(row => ({...row, status: 'NOT-APPLICABLE'})))) {
    failures.push('an all-NOT-APPLICABLE row must be terminal')
  }
  for (const status of ['PARTIAL', 'FAIL', 'VOID', 'UNBUILT']) {
    const perturbed = [{...green[0], status}, ...green.slice(1)]
    if (terminalFor(perturbed)) failures.push(`a row containing ${status} must NOT be terminal`)
  }
  if (terminalFor([])) failures.push('a module with no gate rows at all must NOT be terminal')

  // A not-applicable disposition is the ONLY way a module reaches terminal status without a PASS, so
  // the fields that make it a human decision are the fields that must be enforced. Driven through
  // declaredDisposition() itself: a canary that reimplements the rule reports a DEAD gate as healthy.
  const dispositionCases = [
    ['no rationale and no decider', {gates: {assertVisualParity: {disposition: 'not-applicable'}}}],
    ['a rationale but no decider', {gates: {assertVisualParity: {disposition: 'not-applicable', rationale: 'renders nothing'}}}],
    ['a decider but no rationale', {gates: {assertVisualParity: {disposition: 'not-applicable', decidedBy: 'A Person'}}}],
  ]
  for (const [label, profile] of dispositionCases) {
    let threw = false
    try { declaredDisposition(profile, 'assertVisualParity') } catch { threw = true }
    if (!threw) failures.push(`a not-applicable disposition with ${label} must throw`)
  }
  const complete = {gates: {assertVisualParity: {disposition: 'not-applicable', rationale: 'renders nothing', decidedBy: 'A Person'}}}
  if (declaredDisposition(complete, 'assertVisualParity')?.[0] !== 'NOT-APPLICABLE') {
    failures.push('a complete not-applicable disposition must read NOT-APPLICABLE')
  }
  if (declaredDisposition(complete, 'assertAccessible') !== null) {
    failures.push('a gate with no disposition must fall through to its own verdict, not inherit a sibling disposition')
  }
  if (declaredDisposition({gates: {assertVisualParity: {disposition: 'required'}}}, 'assertVisualParity') !== null) {
    failures.push('an explicitly required gate must fall through to its own verdict')
  }

  if (failures.length > 0) {
    process.stderr.write(`canary FAIL:\n${failures.map(failure => `  ${failure}`).join('\n')}\n`)
    process.exit(1)
  }
  process.stdout.write('canary OK -- PASS and NOT-APPLICABLE are terminal; PARTIAL, FAIL, VOID, UNBUILT and an empty row are not\n')
  process.exit(0)
}

if (argv.includes('--canary')) canary()

const receipt = build()
process.exitCode = receipt.status === 'FAIL' ? 1 : 0
const receiptDirectory = resolve(platformRoot, 'docs/evidence/gate-model')
mkdirSync(receiptDirectory, {recursive: true})
writeFileSync(resolve(receiptDirectory, 'ui-gate-model.json'), `${JSON.stringify(receipt, null, 2)}\n`)

if (argv.includes('--json')) {
  process.stdout.write(`${JSON.stringify(receipt, null, 2)}\n`)
} else {
  const terminal = receipt.modules.filter(module => module.terminal).length
  process.stdout.write(`${receipt.modules.length} modules, ${terminal} terminal\n`)
  process.stdout.write(`${receipt.status}: ${designReviewMessage(receipt.designReview)}\n`)
  process.stdout.write(`${Object.entries(receipt.tally).sort().map(([status, count]) => `${status}=${count}`).join('  ')}\n`)

  // Which gate is holding the most modules back is the worklist, and it is derived here rather than
  // counted by hand — ticket 098 acceptance 7: the sweep's output IS the triage.
  const blocking = {}
  for (const module of receipt.modules) {
    for (const row of module.gates) {
      if (!TERMINAL_STATUSES.has(row.status)) blocking[row.id] = (blocking[row.id] ?? 0) + 1
    }
  }
  // A gate with a declared PARTIAL ceiling is marked, because the count beside it means something
  // different: those modules are not waiting on fixture work, they are unreachable until the gate
  // itself gains a PASS branch. Printing both in one undifferentiated column is what let five
  // structural walls read as a backlog.
  const {PARTIAL_CEILINGS} = createGateModel(platformRoot)
  process.stdout.write('\nmodules blocked, by gate\n')
  for (const [id, count] of Object.entries(blocking).sort((a, b) => b[1] - a[1])) {
    const note = PARTIAL_CEILINGS[id] ? '  <- cannot return PASS yet' : ''
    process.stdout.write(`  ${id.padEnd(28)}${String(count).padEnd(6)}${note}\n`)
  }
  const capped = Object.keys(PARTIAL_CEILINGS).filter(id => blocking[id])
  if (capped.length > 0) {
    process.stdout.write(`\n${capped.length} gate(s) carry a declared ceiling. Until each gains a PASS branch, no module they\n`)
    process.stdout.write('apply to can reach terminal status, however much fixture work is done:\n')
    for (const id of capped) process.stdout.write(`  ${id}\n    ${PARTIAL_CEILINGS[id]}\n`)
  }
}
