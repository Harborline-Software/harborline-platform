import assert from 'node:assert/strict'
import test from 'node:test'

import {failingTests} from '../run-tooling-selftests.mjs'

// The generic shape: TAP reports a failure WHERE it happened and its counts at the very END, so a
// blind tail of the last N lines is always the summary and never a name. The phase-4 receipt run on
// macpro (2026-09-05) failed this step twice and the bounded gate log said only "1 of 121 failed".
// These tests are red if the reporting goes back to tailing.
const tap = [
  'TAP version 13',
  '# Subtest: tooling/tests/example.test.mjs',
  '    # Subtest: a green one',
  '    ok 1 - a green one',
  '    # Subtest: the one that broke',
  '    not ok 2 - the one that broke',
  '      ---',
  "      failureType: 'testCodeFailure'",
  '      error: |-',
  '        Expected values to be strictly equal',
  '      ...',
  '    1..2',
  'not ok 1 - tooling/tests/example.test.mjs',
  '  ---',
  "  failureType: 'subtestsFailed'",
  '  ...',
  '1..1',
  '# tests 2',
  '# pass 1',
  '# fail 1',
].join('\n')

test('a failing self-test is named, with its diagnostic', () => {
  const failures = failingTests(tap)
  assert.deepEqual(failures.map(failure => failure.name),
    ['the one that broke', 'tooling/tests/example.test.mjs'])
  assert.match(failures[0].detail, /Expected values to be strictly equal/)
})

test('the summary tail TAP ends with carries no name', () => {
  // Negative of the negative: the old reporting printed this slice and nothing else.
  const tail = tap.split('\n').slice(-6).join('\n')
  assert.doesNotMatch(tail, /the one that broke/)
})

test('a green run names nothing', () => {
  assert.deepEqual(failingTests('TAP version 13\nok 1 - fine\n1..1\n# fail 0\n'), [])
})
