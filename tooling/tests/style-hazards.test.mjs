// The gallery catches these in seven minutes and a browser; the source shows them in under a second.
// Every rule here exists because a defect of that exact shape cost a full gate cycle on 2026-08-26:
// a foreground token absent from the registry falling to MarkText (3.19:1 black on red, 58 axe
// violations), the same fix then reading 2.28:1 in the dark theme because one literal cannot serve
// both, an SVG spinner inside role="status", and a module styled in one lane only.
import assert from 'node:assert/strict'
import test from 'node:test'
import {execFileSync} from 'node:child_process'
import {resolve} from 'node:path'

const root = resolve(import.meta.dirname, '../..')
const scanner = resolve(root, 'tooling/gates/scan-style-hazards.mjs')

const run = args => {
  try {
    return {status: 0, out: execFileSync(process.execPath, [scanner, ...args], {cwd: root, encoding: 'utf8'})}
  } catch (error) {
    return {status: error.status ?? 1, out: `${error.stdout ?? ''}${error.stderr ?? ''}`}
  }
}

test('every rule fires on a fixture built to trip it', () => {
  const {status, out} = run(['--canary'])
  assert.equal(status, 0, out)
  assert.match(out, /canary OK/)
})

test('the repository carries no unacknowledged style hazard', () => {
  const {status, out} = run([])
  assert.equal(status, 0, out)
  assert.match(out, /0 unacknowledged style hazards/)
})

// Retired exemptions must disappear once the corresponding stylesheet is discoverable.
test('retired style-hazard exemptions are not reported', () => {
  const {out} = run([])
  assert.doesNotMatch(out, /acknowledged, tracked by ticket/)
  assert.doesNotMatch(out, /ticket 140/)
})
