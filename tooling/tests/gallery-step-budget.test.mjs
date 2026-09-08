import assert from 'node:assert/strict'
import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { resolve } from 'node:path'
import test from 'node:test'

import { runFixtureStep, spawnWithBudget } from '../resolve-command.mjs'
import { fixturesBudgetMs, run as prepareRun } from '../prepare-galleries.mjs'
import { run as galleryRun } from '../run-gallery-gate.mjs'

// Ticket 275: a gallery-gate step used to have no timeout at all, so a wedged spawn (npm ci,
// dotnet restore --no-cache, playwright install) looked identical to one that was merely slow --
// no output, no CPU, forever. A tiny budget against a child that outlives it must kill the child
// AND fail by a name that says which step, which budget and which command, so the failure is
// diagnosable from the gate log alone.
const TINY_BUDGET_MS = 300
const OUTLIVES_BUDGET_ARGS = ['-e', 'setTimeout(() => {}, 10000)']

function withTinyBudget(body) {
  process.env.HARBORLINE_GALLERY_STEP_BUDGET_MS = String(TINY_BUDGET_MS)
  try {
    return body()
  } finally {
    delete process.env.HARBORLINE_GALLERY_STEP_BUDGET_MS
  }
}

test('spawnWithBudget kills a child that exceeds its budget', () => {
  withTinyBudget(() => {
    const started = performance.now()
    const result = spawnWithBudget(process.execPath, OUTLIVES_BUDGET_ARGS)
    const elapsedMs = performance.now() - started
    assert.equal(result.error?.code, 'ETIMEDOUT')
    assert.ok(elapsedMs < 8_000, `expected the kill to land well under the child's 10000ms lifetime, took ${elapsedMs}ms`)
  })
})

test('a child that finishes within budget is not treated as timed out', () => {
  process.env.HARBORLINE_GALLERY_STEP_BUDGET_MS = '5000'
  try {
    const result = spawnWithBudget(process.execPath, ['-e', '1'])
    assert.equal(result.error, undefined)
    assert.equal(result.status, 0)
  } finally {
    delete process.env.HARBORLINE_GALLERY_STEP_BUDGET_MS
  }
})

// The named failure, driven through the runners' own run() -- one layer above spawnWithBudget.
// Without these, deleting the ETIMEDOUT throw from either runner leaves the suite green while the
// gate silently regresses to `exitCode: null, failureOutput: "\n"` -- the exact indistinguishable
// silence this ticket exists to remove.
test('a gallery gate step past its budget fails by step id, budget and command', () => {
  withTinyBudget(() => {
    assert.throws(
      () => galleryRun('react-gallery-typecheck', process.execPath, OUTLIVES_BUDGET_ARGS),
      /react-gallery-typecheck exceeded its 300ms budget and was killed: .*setTimeout/)
  })
})

test('a gallery gate run refuses an unavailable browser with Playwright\'s reason', () => {
  const unavailableBrowsers = mkdtempSync(resolve(tmpdir(), 'hlp-unavailable-browser-'))
  try {
    assert.throws(
      () => galleryRun(
        'playwright-browser-launch',
        process.execPath,
        ['gallery/tests/verify-browser.mjs'],
        undefined,
        { PLAYWRIGHT_BROWSERS_PATH: unavailableBrowsers },
      ),
      error => {
        assert.match(error.message, /Playwright Chromium unavailable/)
        assert.match(error.message, /Executable doesn't exist/)
        assert.doesNotMatch(error.message, /"browserTests": 0[\s\S]*"status": "PASS"/)
        return true
      },
    )
  } finally {
    rmSync(unavailableBrowsers, { recursive: true, force: true })
  }
})

test('a gallery prepare step past its budget fails by name, budget and command', () => {
  withTinyBudget(() => {
    assert.throws(
      () => prepareRun(process.execPath, OUTLIVES_BUDGET_ARGS),
      /prepareGalleries step exceeded its 300ms budget and was killed: .*setTimeout/)
  })
})

// The ~20 serial restores inside verify-package-fixtures.mjs are where the recorded 66 minutes of
// silence actually lived: each one is budgeted and named individually, so a wedge in restore 14 of
// 20 says which command, rather than "the whole fixture step".
test('a package fixture restore past its budget names the command that wedged', () => {
  withTinyBudget(() => {
    assert.throws(
      () => runFixtureStep(process.execPath, OUTLIVES_BUDGET_ARGS),
      /package fixture step exceeded its 300ms budget and was killed: .*setTimeout/)
  })
})

// The step budget is 900s; verify-package-fixtures' only recorded duration is 66 minutes in a run
// that went green. Governing it with the step budget would kill slow-but-correct work, so it has
// its own, and that one must stay above the recorded worst case.
test('the package fixture step has its own budget, above the recorded 66 minutes', () => {
  assert.ok(fixturesBudgetMs() > 66 * 60_000,
    `fixtures budget ${fixturesBudgetMs()}ms is not above the recorded 66-minute run`)
  process.env.HARBORLINE_GALLERY_FIXTURES_BUDGET_MS = '1234'
  try {
    assert.equal(fixturesBudgetMs(), 1234)
  } finally {
    delete process.env.HARBORLINE_GALLERY_FIXTURES_BUDGET_MS
  }
})
