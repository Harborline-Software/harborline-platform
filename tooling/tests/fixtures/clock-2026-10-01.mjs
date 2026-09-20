// T-631. A `node --import` shim that moves this process's clock past 2026-09-30, the deadline the
// owner's ruling of 2026-09-20 withdrew.
//
// It exists so the acceptance line "no date involved" is proved by running rather than by
// argument. Before the ruling, `design-review-status.mjs` amnestied an expired review only while
// `now < new Date(`${deadline}T00:00:00.000Z`)`; under this shim that comparison is false, so any
// surviving copy of it -- here, in a caller that passes its own `now`, or in a check added later
// -- flips all 51 backlogged modules to FAIL and the sweep exits 1. A sweep that still exits 0
// under a clock a year forward is a sweep that reads no date.
//
// Deliberately only the two entry points that read the wall clock. Parsing and formatting are
// untouched, so a test can still write a fixed date and mean it.
const FROZEN_NOW = Date.parse('2026-10-01T00:00:00.000Z')
const RealDate = Date

class ShimmedDate extends RealDate {
  constructor(...args) {
    super(...(args.length === 0 ? [FROZEN_NOW] : args))
  }

  static now() {
    return FROZEN_NOW
  }
}

globalThis.Date = ShimmedDate
