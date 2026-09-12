#!/usr/bin/env node
import {writeFileSync} from 'node:fs'
// The phase-4 gate's contract, in one place.
//
// requiredStepIds was written out verbatim in three files: run-phase-4-gate.mjs (which produces the
// result), validate-repository.mjs (which asserts the recorded gate is complete), and
// verify-phase4-receipt.mjs (which the pre-commit hook runs). The cost is recorded in the tree —
// .github/workflows/validate.yml declines to add a CI check to the local gate because doing so
// "means changing requiredStepIds in THREE files, which invalidates every existing receipt".
//
// All three consolidate, including the receipt verifier. Ticket 099 item 5 leaves that open on the
// grounds an independent copy inside a verifier could be a defensible check-the-checker. It is not
// one here: verify-phase4-receipt.mjs's own header says it was vendored because "the migration
// control plane has no remote and is being deleted", not for independence, and it now lives in this
// tree, at this commit, staged by the same hook it guards. There is no independence left to
// preserve — only a third place to forget.
const allStepIds = [
  'root-clean-install', 'npm-clean-install', 'forms-contracts-clean-install', 'rule-runtime-clean-install', 'rule-authoring-clean-install', 'copilot-contracts-clean-install', 'dotnet-restore', 'generation-smoke', 'ui-spec-authority', 'catalog-preflight',
  'tooling-selftests', 'sibling-package-origins', 'prop-vocabulary', 'ui-gate-model', 'build', 'native-tests', 'perf-budgets', 'ui-shared-conformance', 'package-consumers',
  'gallery-gate', 'catalog-final',
]

// The steps that drive real browsers. They are the only ones that need a display, a Playwright
// install and a quiet host, and they are the ones that have been failing on wall clock rather than
// on a product defect -- three timeouts and eleven flaky in one run, zero assertion failures.
//
// The MVP is HEADLESS. Until it is not, a browser parity suite must not decide whether a headless
// change may land. Set HARBORLINE_GATE_HEADLESS=1 and the gate neither runs these nor requires
// them; the scheduled cross-platform job still runs the full set, so nothing stops being tested --
// it stops being in the landing path.
export const browserStepIds = ['gallery-gate']

export const headless = process.env.HARBORLINE_GATE_HEADLESS === '1'

export const requiredStepIds = headless
  ? allStepIds.filter(id => !browserStepIds.includes(id))
  : allStepIds

export function recordPhase4Gate(evidencePath, report) {
  if (report.status !== 'PASS') return false
  writeFileSync(evidencePath, `${JSON.stringify(report, null, 2)}\n`)
  return true
}
