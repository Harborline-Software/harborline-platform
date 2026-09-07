#!/usr/bin/env node
// Vendored from harborline-control/tools/run-vertical-pair.mjs on 2026-08-25.
//
// It measured this repository from outside it, so nothing here could ever fail because of it and no
// module status it produced could mean anything. Same reason eng/run-exact-clone.mjs was vendored
// into harborline-api. The control-repo copy is deleted, not forked: a tool in two places is a
// second register, and this programme has already paid for one of those.
// Ticket 098 phase 2 — drive a pair of modules through all fourteen Tier-1 gates.
//
// The point of a vertical is NOT to collect green ticks. It is to find out which gates can actually
// render a verdict for one component, and the answer is meant to be uncomfortable: a gate with no
// implementation must report UNBUILT, never PASS. Ticket 097 is the receipt for the alternative --
// it was found by running one gate horizontally across 76 modules and produced a wide, shallow
// signal that took two CI campaigns and nine days to diagnose.
//
// Verdicts:
//   PASS      the gate ran for this module and the evidence says so
//   FAIL      the gate ran and disagreed
//   UNBUILT   no implementation exists. NOT a pass. NOT a skip.
//   PARTIAL   an implementation exists but does not cover this module, or covers only one half
//   VOID      assertDeterminism failed, so this lane's visual and functional verdicts are void
//             (ruling 1: a void verdict must be distinguishable from both pass and fail)
//
// Usage: node run-vertical-pair.mjs <platform-root> [moduleA moduleB] [--json|--canary]

import {existsSync, readFileSync} from 'node:fs'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

import {createGateModel} from './gate-rows.mjs'
import {CANARY_ROWS, runCanary} from './vertical-pair-canary.mjs'

// This tool now lives inside the repository it measures: the root is two levels up from here.
const defaultPlatformRoot = `${import.meta.dirname}/../..`

const here = dirname(fileURLToPath(import.meta.url))
const argv = process.argv.slice(2)
const positional = argv.filter(a => !a.startsWith('--'))
const platformRoot = positional[0] ?? defaultPlatformRoot
const pair = positional.length > 2 ? positional.slice(1, 3) : ['hlp.ui.toaster', 'hlp.ui.badge']

// One model, shared with run-ui-gate-model.mjs. The canary below drives it through gateRows() rather
// than a local copy of the rule: an earlier draft recomputed VOID itself, and perturbing the real
// propagation left the canary green -- a canary testing a copy reports a DEAD gate as healthy.
const model = createGateModel(platformRoot)
const {GATES, VOIDED_BY_DETERMINISM, verdictOf, gateRows, contextFor, gate} = model

if (argv.includes('--canary')) {
  // The canary lives in its own module so a self-test builds the SAME context this CLI does; see
  // vertical-pair-canary.mjs for why that matters (the context lost a field and the canary died).
  // The WHOLE model, not a hand-picked subset: picking fields here is the same defect as the
  // hand-written context that killed this canary once already -- runCanary reads model.platformRoot
  // to derive a real surface, and a subset would have hidden that behind an undefined.
  const failures = runCanary(model)
  process.stdout.write(`canary rows exercised:\n${CANARY_ROWS.map(r => `  ${r}`).join('\n')}\n`)
  if (failures.length > 0) {
    process.stderr.write(`canary FAIL:\n${failures.map(f => `  ${f}`).join('\n')}\n`)
    process.exit(1)
  }
  process.stdout.write(`canary OK -- ${CANARY_ROWS.length} rows exercised, none dead\n`)
  process.exit(0)
}

const report = {
  schemaVersion: 1,
  ticket: '098 phase 2',
  pair,
  gateEvidence: gate ? {status: gate.status, testedTree: gate.subject?.testedTree} : null,
  modules: pair.map(moduleId => {
    const ctx = contextFor(moduleId)

    return {
      moduleId,
      scenarios: ctx.catalog?.scenarios.length ?? 0,
      conformanceCases: ctx.conformanceCases,
      gates: gateRows(ctx),
    }
  }),
}
report.tally = report.modules.flatMap(m => m.gates).reduce((t, g) => ({...t, [g.status]: (t[g.status] ?? 0) + 1}), {})
report.unbuilt = [...new Set(report.modules.flatMap(m => m.gates.filter(g => g.status === 'UNBUILT').map(g => g.id)))].sort()

if (argv.includes('--json')) {
  process.stdout.write(`${JSON.stringify(report, null, 2)}\n`)
} else {
  const width = Math.max(...GATES.map(g => g.id.length)) + 2
  process.stdout.write(`ticket 098 phase 2 -- vertical pair: ${pair.join(' + ')}\n`)
  process.stdout.write(`gate evidence: ${report.gateEvidence?.status ?? 'ABSENT'} @ ${report.gateEvidence?.testedTree?.slice(0, 10) ?? '-'}\n\n`)
  process.stdout.write(`${'gate'.padEnd(width)}${pair.map(p => p.replace('hlp.ui.', '').padEnd(10)).join('')}\n`)
  process.stdout.write(`${'-'.repeat(width - 2).padEnd(width)}${pair.map(() => '---------').map(s => s.padEnd(10)).join('')}\n`)
  GATES.forEach((g, i) => {
    const label = (g.parent ? '  ' : '') + g.id
    process.stdout.write(`${label.padEnd(width)}${report.modules.map(m => m.gates[i].status.padEnd(10)).join('')}\n`)
  })
  process.stdout.write(`\ntally: ${Object.entries(report.tally).sort().map(([k, v]) => `${k}=${v}`).join('  ')}\n\nnotes\n`)
  for (const m of report.modules) {
    process.stdout.write(`  ${m.moduleId}  (${m.scenarios} scenarios, ${m.conformanceCases} conformance cases)\n`)
    for (const g of m.gates) if (g.status !== 'PASS') process.stdout.write(`    ${g.status.padEnd(8)} ${g.id}: ${g.note}\n`)
  }
}

// A vertical whose gates are mostly unbuilt is the FINDING, not a failure of this tool. It exits 0
// and says so. Non-zero only when a gate that exists rendered a real FAIL.
process.exitCode = report.modules.flatMap(m => m.gates).some(g => g.status === 'FAIL') ? 1 : 0
