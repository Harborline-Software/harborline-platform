// Oracles: owner-approved advisory scope and literal PR/review examples, not production constants.
import assert from 'node:assert/strict'
import test from 'node:test'
import {resolve} from 'node:path'
import {reuseNoteAdvisories, runPrPreflight} from '../run-pr-preflight.mjs'

const head = '1111111111111111111111111111111111111111'
const other = '2222222222222222222222222222222222222222'
const root = resolve(import.meta.dirname, '../..')
const note = (revision = head) => `## Reuse note
- Scope: substantial
- Revision: ${revision}
- Checked: [implementation](AGENTS.md), [research](https://example.org/research)
- Chosen: extend the existing producer.
- Custom: none; reuse the existing implementation.
`
const check = (body, revision = head) => reuseNoteAdvisories(body, {head: revision, localExists: path => path === 'AGENTS.md'})

test('missing note asks for human scope assessment without classifying code as substantial', () => {
  assert.deepEqual(check('## What changed\nA change.\n'), ['Reuse note missing; reviewer should confirm scope (routine changes need a reason).'])
})

test('complete current substantial note accepts existing local and unchecked external references', () => {
  assert.deepEqual(check(note()), [])
})

test('stale and malformed revisions are advisory findings', () => {
  assert.deepEqual(check(note(other)), ['Reuse note revision does not match the current review head.'])
  assert.deepEqual(check(note('main')), ['Reuse note needs a full 40-digit head revision.'])
  assert.deepEqual(reuseNoteAdvisories(note(), {localExists: () => true}), ['Current review head unavailable; Reuse note revision was not verified.'])
})

test('broken local paths and malformed URI escapes are reported, without fetching URLs', () => {
  assert.deepEqual(check(note().replace('(AGENTS.md)', '(missing.md#section)')), ['Reuse note local reference was not found: missing.md'])
  assert.deepEqual(check(note().replace('(AGENTS.md)', '(%zz.md)')), ['Reuse note local reference is malformed: %zz.md'])
})

test('routine reason is human supplied and needs no research fields', () => {
  assert.deepEqual(check(`## Reuse note\n- Scope: routine: fix one spelling error.\n- Revision: ${head}\n`), [])
  assert.deepEqual(check(`## Reuse note\n- Scope: routine\n- Revision: ${head}\n`), ['Routine scope needs a reason for reviewer assessment.'])
})

test('empty template comments are not research evidence', () => {
  const body = `## Reuse note\n- Scope: substantial\n- Revision: ${head}\n- Checked: <!-- add references -->\n- Chosen: <!-- choose -->\n- Custom: <!-- reason -->\n`
  assert.deepEqual(check(body), ['Substantial Reuse note needs Checked references.', 'Substantial Reuse note needs Chosen approach.', 'Substantial Reuse note needs Custom rationale (or none).'])
})

test('duplicate notes or fields cannot silently select a preferred claim', () => {
  assert.deepEqual(check(note() + note()), ['Multiple Reuse note sections; keep one current note.'])
  assert.deepEqual(check(note() + '- Revision: ' + other + '\n'), ['Reuse note repeats Revision; keep one value.'])
})

test('review text is scoped to its section and local reference fragments are ignored', () => {
  assert.deepEqual(check(note().replace('(AGENTS.md)', '(AGENTS.md#agent-guide)') + '\n## Ticket\n[unrelated](missing.md)\n'), [])
})

test('reuse advisories never change preflight verdict, required commands or failure treatment', () => {
  const calls = []
  const tap = '# tests 1\n# pass 1\n# fail 0\n# cancelled 0\n# skipped 0\n# todo 0\n'
  assert.equal(runPrPreflight(root, (command, args) => { calls.push([command, args]); return {status: 0, stdout: tap} }, {body: note(other), head}), true)
  assert.equal(calls.length, 5)
  assert.deepEqual(calls[0], ['bash', ['eng/verify-boundaries.sh']])
  assert.equal(runPrPreflight(root, () => ({status: 1, stdout: ''}), {body: note(), head}), false)
})

// Literal nested input reproduces the CodeQL annotation without deriving an oracle from the parser.
test('nested comment removal cannot supply Checked references', () => {
  const body = `## Reuse note
- Scope: substantial
- Revision: ${head}
- Checked: <<!-- ignored -->!-- [fake](AGENTS.md) -->
- Chosen: extend the existing producer.
- Custom: none; reuse the existing implementation.
`
  assert.deepEqual(check(body), ['Substantial Reuse note needs Checked references.'])
})

// A link opener is not a reference; expected advisories are independent literal review policy.
test('Checked references require a complete inline link', () => {
  const withChecked = value => note().replace(/^- Checked:.*$/m, '- Checked: ' + value)
  for (const value of ['[implementation](', '[implementation]()', '[implementation](AGENTS.md', '[implementation](<AGENTS.md>', '[implementation](<AGENTS.md)']) {
    assert.deepEqual(check(withChecked(value)), ['Substantial Reuse note needs Checked references.'], value)
  }
  assert.deepEqual(check(withChecked('[implementation](AGENTS.md)')), [])
  assert.deepEqual(check(withChecked('[implementation](<AGENTS.md>)')), [])
})

test('genuine absence explanations remain human-reviewed Checked advisories', () => {
  const body = note().replace(/^- Checked:.*$/m, '- Checked: No existing implementation or prior research covers this new capability; the gap is explained for review.')
  assert.deepEqual(check(body), ['Substantial Reuse note needs Checked references.'])
})
