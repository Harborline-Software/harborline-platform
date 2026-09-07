#!/usr/bin/env node
// Runs the tooling self-test suite and fails CLOSED on an empty match: `node --test` exits 0
// when its glob matches nothing, so a bare glob step would stay green forever if this directory
// were moved or emptied — the exact silent-vanish failure ticket 081's wave-2 review found the
// suite already living in. The reporter is pinned to TAP so the `# key N` summary lines are
// stable to parse regardless of Node's TTY-sensitive default.
//
// The failure output is EXTRACTED, never tailed. TAP puts the `not ok` lines where the failures
// happened and the `# tests N / # fail N` counts at the very end, so a blind tail of the last N
// lines is always the counts and never a name: a phase-4 receipt run on macpro (2026-09-05) failed
// this step twice and the only thing visible in the bounded gate log was "1 of 121 failed". The
// names now go in the JSON report as well as stderr, so they survive every downstream line bound.
import {spawnSync} from 'node:child_process'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

// TAP: `not ok <n> - <name>` optionally followed by an indented YAML diagnostic block. Nested
// subtests are indented, and the file itself gets its own `not ok`, so a failing leaf yields both
// its own name and its file's — which is exactly the pair a reader needs.
export function failingTests(tap) {
  const lines = (tap ?? '').split('\n')
  const failures = []
  for (let index = 0; index < lines.length; index += 1) {
    const match = /^(\s*)not ok \d+ - (.*)$/.exec(lines[index])
    if (!match) continue
    const [, indent, name] = match
    const detail = []
    while (index + 1 < lines.length && lines[index + 1].startsWith(`${indent}  `)) {
      detail.push(lines[index + 1])
      index += 1
    }
    failures.push({name: name.trim(), detail: detail.join('\n')})
  }
  return failures
}

if (import.meta.main) {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
  const run = spawnSync(process.execPath, ['--test', '--test-reporter', 'tap', 'tooling/tests/*.test.mjs'], {
    cwd: root,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  })
  const summary = Object.fromEntries(['tests', 'pass', 'fail', 'cancelled', 'skipped', 'todo']
    .map(key => [key, Number(new RegExp(`^# ${key} (\\d+)$`, 'm').exec(run.stdout ?? '')?.[1] ?? NaN)]))
  const passed = run.status === 0 && summary.tests > 0 && summary.fail === 0
    && summary.cancelled === 0 && summary.skipped === 0 && summary.pass === summary.tests
  const failures = passed ? [] : failingTests(run.stdout)
  process.stdout.write(`${JSON.stringify({
    schemaVersion: 1,
    status: passed ? 'PASS' : 'FAIL',
    counts: summary,
    ...(passed ? {} : {failing: failures.map(failure => failure.name)}),
  }, null, 2)}\n`)
  if (!passed) {
    const detail = failures.map(failure => `not ok - ${failure.name}\n${failure.detail}`).join('\n')
    process.stderr.write(`${detail.split('\n').slice(0, 200).join('\n')}\n${run.stderr ?? ''}\n`)
  }
  process.exitCode = passed ? 0 : 1
}
