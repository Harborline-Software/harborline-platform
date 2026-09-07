#!/usr/bin/env node
// Ticket 268 -- the platform gate's quiet serial performance slot.
//
// WHY A STEP AND NOT A PROPERTY OF THE LOCK. `tooling/phase4-gate-lock.mjs` excludes one thing:
// a second phase-4 GATE on the same repository. It says nothing about the lane builds, `dotnet
// test` runs and editors that share this box -- on the Windows relocation host a gate has never had
// the machine to itself, and the lock cannot give it one without becoming a machine-wide mutex that
// every lane would have to honour. So quiet is not something the lock can promise, and a budget
// that assumes it would be the flapping budget ticket 265 spent four rounds removing.
//
// What the gate CAN promise is serialisation of its own work: `run-phase-4-gate.mjs` spawns its
// steps one at a time, so a step is alone in the gate by construction. Today the budgeted rows do
// not get that -- they ride inside `native-tests`, which runs six suites in parallel and then
// twenty-five `dotnet test` projects in parallel, i.e. the loudest moment of the whole gate. Moving
// them into their own step is the entire mechanism: same rows, same ceilings file, one process at a
// time, nothing else of the gate's in flight.
//
// The rest of the box is measured rather than assumed. Before the rows run, this step samples CPU
// busy fraction over one second; at or under QUIET_BUSY_BUDGET the rows run with
// HARBORLINE_PERF_QUIET=1 and the tests apply their tight (2x quiet p95) ceilings, above it they
// run without it and the loose gate-proof ceilings from ticket 265 apply. Sensitivity is therefore
// available exactly when it is earned, and a busy box costs a gate its detection floor rather than
// a false red. The report records which happened, so a run of gates that never got a quiet window
// is visible instead of silently loose.
//
// PRIOR ART. This is the standard answer to a shared runner. BenchmarkDotNet's own guidance is that
// results from a machine running other work are not comparable and it prints an environment/overhead
// warning rather than tightening its statistics; Criterion.rs likewise documents that its noise
// threshold exists because CI machines are noisy and that a run on a loaded host should be treated
// as advisory. Neither tries to make a loaded measurement precise. Both isolate the measurement and
// then say plainly what conditions produced the number, which is what this step does.
//
// The rows themselves are NOT enumerated here. `tooling/perf-budget-stability.mjs` (ticket 265)
// already owns that list, invokes each target as one serial process, parses the `[perf] row=` line
// and scores a run with no measurement as red. Listing the rows again in the gate would be a second
// copy to forget, so the step drives that tool and records its output verbatim in the gate report.

import {spawnSync} from 'node:child_process'
import {existsSync} from 'node:fs'
import {cpus} from 'node:os'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

import {runnerEnvironment} from './resolve-command.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const stabilityTool = resolve(root, 'tooling/perf-budget-stability.mjs')

// The stated CPU budget. One core's worth of background work on this sixteen-core host is 0.0625;
// the budget is four times that, which admits an editor, a language server and a single lane test
// process but not a concurrent build (a `dotnet build -maxcpucount:6` sits well above it). It is a
// fraction, not a core count, so a two-core CI runner gets the same rule.
export const QUIET_BUSY_BUDGET = 0.25
export const CPU_SAMPLE_MS = 1_000

function cpuTotals() {
  return cpus().reduce((totals, cpu) => {
    for (const [mode, value] of Object.entries(cpu.times)) totals[mode === 'idle' ? 'idle' : 'busy'] += value
    return totals
  }, {idle: 0, busy: 0})
}

/** Busy fraction between two `cpuTotals()` snapshots; 0 when no time elapsed between them. */
export function busyFractionBetween(before, after) {
  const idle = after.idle - before.idle
  const busy = after.busy - before.busy
  return idle + busy > 0 ? busy / (idle + busy) : 0
}

/** Samples the box for `sampleMs` and reports whether the slot is quiet enough for tight ceilings. */
export function observeQuiet(sampleMs = CPU_SAMPLE_MS, budget = QUIET_BUSY_BUDGET) {
  const before = cpuTotals()
  Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, sampleMs)
  const busyFraction = Number(busyFractionBetween(before, cpuTotals()).toFixed(4))
  return {quiet: busyFraction <= budget, busyFraction, budget, sampleMs, cores: cpus().length}
}

function main() {
  if (!existsSync(stabilityTool)) {
    // Loud, not skipped: this step exists to run the budgeted rows, and a missing runner is the
    // silent-vanish failure `run-tooling-selftests.mjs` was written for. The tool lands with
    // ticket 265; ticket 268 is blocked by it and the two land together.
    process.stdout.write(`${JSON.stringify({
      schemaVersion: 1,
      status: 'FAIL',
      reason: 'tooling/perf-budget-stability.mjs is absent (it lands with ticket 265)',
    }, null, 2)}\n`)
    process.exitCode = 1
    return
  }

  // HARBORLINE_PERF_QUIET_BUDGET is a measurement knob, not a gate setting: deriving a ceiling or
  // replaying a mutation means choosing the branch rather than waiting for the box to agree.
  const cpu = observeQuiet(CPU_SAMPLE_MS, Number(process.env.HARBORLINE_PERF_QUIET_BUDGET ?? QUIET_BUSY_BUDGET))
  const args = [stabilityTool, '--runs', String(Number(process.env.HARBORLINE_PERF_RUNS ?? 1))]
  // Both are measurement flags, forwarded verbatim to the tool that owns the target list. The gate
  // passes neither: it runs every budgeted row. They exist so a host that can only run one lane
  // (a mac without the React node_modules, a box without the .NET SDK) can still confirm its rows
  // through this step rather than through a hand-built environment that might not match it.
  if (process.argv.includes('--react-only')) args.push('--react-only')
  const only = process.argv[process.argv.indexOf('--only') + 1]
  if (process.argv.includes('--only') && only) args.push('--only', only)
  const result = spawnSync(process.execPath, args, {
    cwd: root,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    // The signal the ceilings read. Set only when the box measured quiet, so the tight ceilings and
    // the conditions they were derived under are the same claim.
    // set unconditionally: an ambient HARBORLINE_PERF_QUIET=1 must not survive into a loud run.
    env: {...process.env, ...runnerEnvironment, HARBORLINE_PERF_QUIET: cpu.quiet ? '1' : ''},
  })
  let stability
  try {
    stability = JSON.parse(result.stdout)
  } catch {
    stability = undefined
  }
  const passed = result.status === 0 && stability?.stable === true
  process.stdout.write(`${JSON.stringify({
    schemaVersion: 1,
    status: passed ? 'PASS' : 'FAIL',
    ceilings: cpu.quiet ? 'tight' : 'loose',
    cpu,
    stability,
  }, null, 2)}\n`)
  // The child's own stderr carries the per-run lines and any assertion text; stdout stays one JSON
  // document because the gate parses it (pre-review checklist (h)).
  if (!passed) process.stderr.write(`${(result.stdout ?? '').split('\n').slice(-40).join('\n')}\n${result.stderr ?? ''}\n`)
  process.exitCode = passed ? 0 : 1
}

if (import.meta.main) main()
