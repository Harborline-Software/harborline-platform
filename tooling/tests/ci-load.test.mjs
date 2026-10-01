// CI load (owner, 2026-09-29): PR runs and the merge queue share one hosted-runner pool, so a PR run
// that a newer push has superseded is cancelled, and a PR labelled `stacked` skips its heavy PR jobs.
// Neither may touch a merge_group, push, schedule or workflow_dispatch run: the merge group still runs
// the full gate before anything lands, which is what makes a skipped-as-green PR context acceptable.
import assert from 'node:assert/strict'
import {readdirSync, readFileSync} from 'node:fs'
import path from 'node:path'
import test from 'node:test'

const dir = path.resolve(import.meta.dirname, '../../.github/workflows')
const read = file => readFileSync(path.join(dir, file), 'utf8').replaceAll('\r\n', '\n')
const guard = (text, job) => text.split(`\n  ${job}:\n`)[1].split(/\n    runs-on:/)[0]
const STACKED = "!contains(github.event.pull_request.labels.*.name, 'stacked')"
const PR_LABELS = /\bgithub\s*\.\s*event\s*\.\s*pull_request\s*(?:\.\s*labels\b|\[\s*['"]labels['"]\s*\])/
const assertPrLabelReads = (text, file) => assert.doesNotMatch(
  text.replaceAll(STACKED, ''), PR_LABELS,
  `${file}: reads PR labels outside the negated stacked guard`,
)

// The jobs a pull request runs that cost a runner. sbom and dependency-review stay: both are cheap.
const heavy = {
  'verify.yml': ['phase-4-gate'],
  'stryker.yml': ['stryker'],
  'strykerjs.yml': ['strykerjs'],
}
const aggregators = {'verify.yml': ['verify']}

test('every pull_request workflow cancels its superseded PR run and only that', () => {
  for (const file of readdirSync(dir).filter(name => /\.ya?ml$/.test(name))) {
    const text = read(file)
    if (!/^ {2}pull_request:/m.test(text)) continue
    const block = text.match(/^concurrency:\n((?: {2}.*\n)+)/m)?.[1]
    assert.ok(block, `${file}: no workflow-level concurrency`)
    assert.match(block, /group: .*github\.event\.pull_request\.number/, `${file}: the group is not keyed on the PR`)
    assert.match(block, /cancel-in-progress: \$\{\{ github\.event_name == 'pull_request' \}\}/, `${file}: cancels more than PR runs`)
  }
})

test('a stacked PR skips its heavy PR jobs and its aggregator; a draft skips the heavy jobs', () => {
  for (const [file, jobs] of Object.entries(heavy)) {
    const text = read(file)
    for (const job of jobs) {
      assert.ok(guard(text, job).includes(STACKED), `${file} ${job}: no stacked skip`)
      assert.ok(guard(text, job).includes('github.event.pull_request.draft == false'), `${file} ${job}: no draft guard`)
    }
  }
  for (const [file, jobs] of Object.entries(aggregators)) {
    for (const job of jobs) assert.ok(guard(read(file), job).endsWith(`\n    if: always() && ${STACKED}`), `${file} ${job}: not always() plus the stacked skip`)
  }
})

test('PR labels are only read in negated stacked guards; runner outputs are independent', () => {
  // A merge_group, push, schedule or dispatch payload carries no pull_request labels: contains() is
  // false there, so the negation is true and the label cannot skip (or enable) anything outside a PR.
  for (const file of readdirSync(dir).filter(name => /\.ya?ml$/.test(name))) {
    const text = read(file)
    assertPrLabelReads(text, file)
  }
})

// Oracle: the owner contract above constrains PR labels, not runner-routing outputs.
test('PR-label contract accepts runner outputs and rejects unsafe PR-label reads', () => {
  const routing = 'labels: ${{ steps.route.outputs.labels }}\nruns-on: ${{ fromJSON(needs.workload-route.outputs.labels) }}'
  assert.doesNotThrow(() => assertPrLabelReads(routing, 'runner routing'))
  assert.doesNotThrow(() => assertPrLabelReads(`${routing}\nif: ${STACKED}`, 'stacked guard plus routing'))
  for (const unsafe of [
    "contains(github.event.pull_request.labels.*.name, 'stacked')",
    'github.event.pull_request.labels[0].name',
    "github.event.pull_request['labels'][0].name",
    'github . event . pull_request . labels[0].name',
    `${STACKED} || contains(github.event.pull_request.labels.*.name, 'run-heavy')`,
  ]) assert.throws(() => assertPrLabelReads(unsafe, 'unsafe fixture'), /reads PR labels outside/)
})
