// The named flake registry, platform half (ticket 284 slice 2). Pure, so
// tooling/tests/flake-registry.test.mjs drives every rule without a gallery run.
//
// Slice 1 gave the gallery receipt `flakyTests`: the NAMES of the specs that failed and then passed
// on their one retry. A name can be owned; a count cannot. This module is what owning means. Every
// row must be EXACT (the spec title verbatim, as the Playwright reporter prints it), OWNED (a ticket
// answerable for removing it), DATED (first seen) and EXPIRING. An unowned or expired row reddens the
// gate, so "known flaky" cannot decay into a permanent exemption list.
//
// The rule that gives the registry teeth is the other direction: a spec that passed only on retry and
// is NOT registered is red exactly the way a failure is. Retries are otherwise a way to make an
// intermittent regression disappear.
//
// Mirrors eng/flake-registry.mjs in harborline-api, deliberately: same row shape, same rules, one
// retry, a count ratchet, validation before any rescue. The rows live in tooling/flake-registry.json
// because a gallery spec title is not a row in any file the platform gate already pins.

// The ratchet. Unlike the api half's ceiling this is an EQUALITY: the literal must equal the real
// row count in both directions. A ceiling that only catches growth drifts upward silently once rows
// are removed, and then the next three registrations are free (284 slice 1 review, MINOR 2). Moving
// this literal is the reviewable diff line that pays for a registration.
export const REGISTERED_FLAKE_COUNT = 1

// ONE identical retry. playwright.config's `retries: 1` is the enforcement; this is the statement of
// it that the registry validates against, so a config bump has to argue with a named constant.
export const RETRY_LIMIT = 1

const DATE = /^\d{4}-\d{2}-\d{2}$/

// `today` is passed in, never read from the clock here: a validator that reads the clock cannot be
// tested against its own expiry boundary.
export function validateFlakeRegistry(rows, today, ceiling = REGISTERED_FLAKE_COUNT) {
  const problems = []
  if (!Array.isArray(rows)) return ['tooling/flake-registry.json: rows is not an array']
  if (!DATE.test(today ?? '')) return [`today must be YYYY-MM-DD, got ${JSON.stringify(today)}`]
  if (rows.length !== ceiling) {
    problems.push(`the flake registry has ${rows.length} rows but REGISTERED_FLAKE_COUNT is ${ceiling}; `
      + 'move the literal in tooling/flake-registry.mjs in the same commit, with the owning ticket')
  }
  const seen = new Set()
  for (const [index, row] of rows.entries()) {
    const at = `rows[${index}]`
    const name = typeof row?.test === 'string' ? row.test.trim() : ''
    if (!name) { problems.push(`${at}: no exact spec title`); continue }
    if (seen.has(name)) problems.push(`${at}: duplicate registration of "${name}"`)
    seen.add(name)
    if (!(typeof row.owner === 'string' && row.owner.trim())) problems.push(`${at} ("${name}"): no owner ticket`)
    if (!DATE.test(row.firstSeen ?? '')) problems.push(`${at} ("${name}"): firstSeen must be YYYY-MM-DD`)
    if (!DATE.test(row.expires ?? '')) problems.push(`${at} ("${name}"): expires must be YYYY-MM-DD`)
    else {
      if (DATE.test(row.firstSeen ?? '') && row.expires <= row.firstSeen) {
        problems.push(`${at} ("${name}"): expires ${row.expires} is not after firstSeen ${row.firstSeen}`)
      }
      // ISO dates compare correctly as strings, which is the whole reason the format is pinned.
      if (row.expires < today) {
        problems.push(`${at} ("${name}"): registration expired ${row.expires} (owner ${row.owner ?? 'none'}); `
          + 'fix the flake or re-register it with a new expiry and a reason')
      }
    }
    if ((row.retryLimit ?? RETRY_LIMIT) !== RETRY_LIMIT) {
      problems.push(`${at} ("${name}"): retryLimit must be ${RETRY_LIMIT} (one identical retry), got ${row.retryLimit}`)
    }
  }
  return problems
}

// A flake nobody registered is a failure. Exact titles, never a prefix or a pattern: a pattern would
// let one registration cover specs nobody looked at.
export function unregisteredFlakes(flakyTests, rows) {
  const registered = new Set((rows ?? []).map(row => row?.test).filter(name => typeof name === 'string'))
  return [...(flakyTests ?? [])].filter(title => !registered.has(title)).sort()
}

// What the receipt records for a flake that IS registered: who owns it, when it expires, and both
// outcomes of the retry (`attempts`, read from the reporter by observeGalleryRun) — a rescue that is
// still visible as a failure followed by a pass, not a silently green test.
export function registeredFlakeRecords(flakyTests, rows, attempts = {}) {
  const byName = new Map((rows ?? []).map(row => [row?.test, row]))
  return [...(flakyTests ?? [])]
    .filter(title => byName.has(title))
    .sort()
    .map(title => {
      const row = byName.get(title)
      return {test: title, owner: row.owner, firstSeen: row.firstSeen, expires: row.expires, outcomes: attempts[title] ?? []}
    })
}
