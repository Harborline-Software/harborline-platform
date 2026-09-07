// Ticket 265. A performance budget written as a flat millisecond constant is a budget for one
// machine. The same six renders of 10,000 rows that cost 106 ms on the Windows relocation host
// cost 360 ms on the 2016 Mac in the gate, so a constant picked on one host is either red on the
// slow host or blind on the fast one.
//
// Fix 1 answered that with a ratio: time a synthetic reference workload interleaved with the
// measured work in the same process and budget `k * reference`. Review round 2 measured the
// reference itself and rejected it: on ONE quiet Windows host the median of the identical fixed
// 1000-pass reference ranged 26.56 - 91.38 ms across runs, a 3.4x spread, while the measured
// bodies moved only ~1.6x. Almost all of the ratio's variance was coming out of the denominator,
// so the ratio flapped red at rest (2 of 6 runs on the React data-grid row, 2 of 3 on AppShell)
// with no regression present. A denominator that noisy cannot be a gate, and no amount of
// re-deriving k fixes it -- the statistic is wrong, not the constant.
//
// Fix 2, therefore: the React rows drop the ratio and keep ONE absolute ceiling each, derived by
// measurement from the SLOWEST host that runs gates (see each call site for the number, the host
// and the detection floor that follows from it). What survives here is only the sampling
// discipline that did hold up -- discard warm-up rounds, then take the MEDIAN of several rounds,
// never a single sample and never the best of N.

export interface Measurement {
  /** Median milliseconds of the measured body over the sampled rounds. */
  readonly median: number
  /** Every sampled round, in order, for a call site that wants to print them. */
  readonly samples: readonly number[]
}

const ROUNDS = 9
const WARM_UP_ROUNDS = 2

function median(values: readonly number[]): number {
  const sorted = [...values].sort((left, right) => left - right)
  const middle = sorted.length >> 1
  return sorted.length % 2 === 1 ? sorted[middle]! : (sorted[middle - 1]! + sorted[middle]!) / 2
}

/**
 * Runs `body` `rounds` times after `WARM_UP_ROUNDS` discarded warm-up rounds and returns the
 * median elapsed milliseconds. The warm-up rounds exist because the first invocations in the
 * process pay the JIT tiers of the whole render path; the median (not the mean, not the best
 * sample) exists because one GC pause or one co-tenant scheduling slice in nine rounds must not
 * move the number the ceiling is compared against.
 */
export function measureMedian(body: () => void, rounds: number = ROUNDS): Measurement {
  for (let warm = 0; warm < WARM_UP_ROUNDS; warm += 1) body()
  const samples: number[] = []
  for (let round = 0; round < rounds; round += 1) {
    const started = performance.now()
    body()
    samples.push(performance.now() - started)
  }
  return { median: median(samples), samples }
}

/**
 * The one line every budgeted row writes to stderr. `tooling/perf-budget-stability.mjs` parses it
 * to collect the samples the ceilings are derived from and to prove the rows do not flap.
 */
export function reportRow(row: string, measured: number): void {
  // This module is part of a browser package whose declaration build runs with no node types, so
  // `process` is looked up off `globalThis` and typed here rather than pulling @types/node into a
  // package that must compile without it. The perf rows only ever run under vitest on node, where
  // the write happens; anywhere else the line is simply not emitted.
  const host = globalThis as { process?: { stderr?: { write(chunk: string): void } } }
  host.process?.stderr?.write(`[perf] row=${row} elapsed=${measured.toFixed(1)}\n`)
}

/**
 * Ticket 268. The ceiling a budgeted row applies. `loose` is the gate-proof number derived under a
 * concurrent gate (ticket 265 fix 3); `quiet` is 2x the quiet p95 of the slowest host (fix 2).
 * The gate's `perf-budgets` step sets HARBORLINE_PERF_QUIET=1 only when it has the box to itself
 * and the box measured quiet, so the tight number is applied exactly under the conditions it was
 * derived under. Everywhere else -- inside the parallel native step, on a developer machine mid
 * build -- the loose number applies and the row still catches the six-to-thirteen-times class.
 */
export function ceilingMs(loose: number, quiet: number): number {
  // Same reason as `reportRow` above: this package's declaration build runs with `"types": []`,
  // so `process` is read off `globalThis` rather than as a global (265 fix 5, TS2591). Absent
  // `process` -- a browser -- the loose ceiling applies, which is the safe direction.
  const host = globalThis as { process?: { env?: Record<string, string | undefined> } }
  return host.process?.env?.HARBORLINE_PERF_QUIET === '1' ? quiet : loose
}
