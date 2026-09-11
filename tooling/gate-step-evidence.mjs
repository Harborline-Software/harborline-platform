import {createHash} from 'node:crypto'
import {execFileSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import {designReviewMessage, designReviewSummary} from './gates/design-review-status.mjs'

// Every path a reusable step reads. Under-declaring is unsound -- the step would reuse a result its
// inputs no longer justify -- so the script half of each list is checked by
// tooling/tests/gate-step-inputs.test.mjs, which walks the entry script's relative-import and
// spawned-path closure and fails on anything the declaration is missing.
//
// The data half is deliberately coarse. Over-declaring only costs a refusal; the whole point is to
// stop refusing on `tooling`, which every gate-tooling commit touches and no step's result depends
// on beyond the few scripts it actually runs.
const BUILD_INPUTS = [
  'Harborline.Platform.slnx',
  'Directory.Build.props',
  'Directory.Packages.props',
  'global.json',
  'package.json',
  'catalog',
  'conformance',
  'projections',
  'specs',
]

// Only three steps are declared. Measured on tree e9a7cab, the other twelve total 17 seconds of a
// 20-minute gate, and six of those are installs and builds whose side effects later steps consume
// -- reusing them in the receipt's fresh worktree would leave the tree without the node_modules,
// restored packages, and packed artifacts that `build`, `package-consumers` and `gallery-gate`
// read. These three cost 980 of the gate's 1200 seconds and produce nothing a later step needs.
export const reusableStepEntryScript = {
  'native-tests': 'tooling/run-native.mjs',
  'ui-shared-conformance': 'tooling/run-shared.mjs',
  'gallery-gate': 'tooling/run-gallery-gate.mjs',
}

export const reusableStepInputs = {
  'native-tests': [
    ...BUILD_INPUTS,
    'tooling/run-native.mjs',
    'tooling/coverage.mjs',
    'tooling/coverage.runsettings',
    'Directory.Build.targets',
    'tooling/parse-node-test-count.mjs',
    'tooling/resolve-command.mjs',
    'tooling/resolve-dotnet.mjs',
  ],
  'ui-shared-conformance': [
    ...BUILD_INPUTS,
    'tooling/run-shared.mjs',
    'tooling/resolve-command.mjs',
    'tooling/resolve-dotnet.mjs',
  ],
  'gallery-gate': [
    ...BUILD_INPUTS,
    'gallery',
    'tooling/run-gallery-gate.mjs',
    'tooling/gallery-baseline-store.mjs',
    // Ticket 284 slice 2: the registry decides whether a retried spec reddens the gate, so a changed
    // registry (rows as much as rules) must invalidate a reused PASS.
    'tooling/flake-registry.mjs',
    'tooling/flake-registry.json',
    'tooling/gallery-observations.mjs',
    'tooling/gallery-parity-coverage.mjs',
    'tooling/gallery-shard-merge.mjs',
    'tooling/prepare-galleries.mjs',
    'tooling/validate-gallery.mjs',
    'tooling/verify-package-fixtures.mjs',
    'tooling/run-galleries.mjs',
    'tooling/package-contribution-policy.mjs',
    'tooling/package-fixture-selection.mjs',
    'tooling/package-version.mjs',
    'tooling/resolve-appshell-feed.mjs',
    'tooling/resolve-command.mjs',
    'tooling/resolve-dotnet.mjs',
  ],
}

export function hashStepInputs({repositoryRoot, testedTree, stepId}) {
  const pathspecs = reusableStepInputs[stepId]
  if (!pathspecs) throw new Error(`${stepId} has no declared inputs`)
  const bytes = execFileSync(
    'git',
    ['ls-tree', '-r', '-z', '--full-tree', testedTree, '--', ...pathspecs],
    {cwd: repositoryRoot, encoding: 'buffer', maxBuffer: 256 * 1024 * 1024},
  )
  if (bytes.length === 0) throw new Error(`${stepId} declared inputs matched no tested-tree files`)
  return createHash('sha256').update(bytes).digest('hex')
}

export function loadPreviousPassEvidence(evidencePath) {
  try {
    const gate = JSON.parse(readFileSync(evidencePath, 'utf8'))
    if (gate.schemaVersion !== 3 || gate.phase !== 4 || gate.status !== 'PASS') {
      return {reason: 'recorded gate evidence is not a schema-v3 phase-4 PASS'}
    }
    return {gate}
  } catch (error) {
    const detail = error instanceof Error ? error.message : String(error)
    return {reason: `recorded gate evidence is absent or unreadable: ${detail}`}
  }
}

export function decideStepReuse({stepId, inputHash, previousPass}) {
  if (!previousPass?.gate) return {mode: 'run-step', reason: previousPass?.reason ?? 'recorded gate evidence is unavailable'}
  if (previousPass.gate.schemaVersion !== 3 || previousPass.gate.phase !== 4 || previousPass.gate.status !== 'PASS') {
    return {mode: 'run-step', reason: 'recorded gate evidence is not a schema-v3 phase-4 PASS'}
  }
  const previous = previousPass.gate.results?.find(result => result.id === stepId)
  if (!previous?.passed || previous.exitCode !== 0 || !previous.report) {
    return {mode: 'run-step', reason: 'previous step evidence is not a complete PASS'}
  }
  if (typeof previous.inputHash !== 'string') {
    return {mode: 'run-step', reason: 'previous step inputHash is absent'}
  }
  if (previous.inputHash !== inputHash) {
    return {mode: 'run-step', reason: 'declared input hash changed'}
  }
  if (typeof previousPass.gate.subject?.testedTree !== 'string') {
    return {mode: 'run-step', reason: 'previous gate testedTree is absent'}
  }
  return {mode: 'reuse', previous}
}

// A step declared JSON must PRINT a JSON document. Exit 0 with empty stdout means the script body
// never ran -- the failure mode a main-module guard regression produces under a symlinked or
// junctioned checkout -- and the gate used to record that as a pass with an absent report. A scan
// that scanned nothing is not a pass.
export function evaluateStepStdout({stepId, json, status, stdout, designReviewOptions}) {
  if (!json) return {status}
  if (!stdout.trim()) {
    return {status: status === 0 ? 1 : status, failure: 'step produced no stdout; a JSON step that prints nothing did not run'}
  }
  try {
    const report = JSON.parse(stdout)
    if (stepId === 'ui-gate-model') {
      report.designReview = designReviewSummary(report.modules, designReviewOptions)
      report.status = report.designReview.status
      if (report.status === 'FAIL') return {status: 1, report,
        failure: designReviewMessage(report.designReview)}
    }
    return {status, report}
  } catch {
    return {status: 1, failure: 'step stdout is not a JSON document'}
  }
}
