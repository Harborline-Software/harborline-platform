#!/usr/bin/env node

import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'

import { galleryParityCoverage } from '../gallery-parity-coverage.mjs'

const parity = ['hlp.ui.example.quality.visual-parity']
const catalogs = () => [{
  moduleId: 'hlp.ui.example',
  scenarios: [
    { id: 'example.theme-light', sourceQualityCaseIds: parity },
    { id: 'example.month-agenda', sourceQualityCaseIds: ['hlp.ui.example.quality.accessibility'] },
  ],
}]
const register = () => ({ modules: { 'hlp.ui.example': { reason: 'recorded gap', uncompared: ['example.month-agenda'] } } })

const clean = galleryParityCoverage(catalogs(), register())
assert.deepEqual(clean.errors, [])
assert.deepEqual(clean.totals, { modules: 1, scenarios: 2, compared: 1, uncompared: 1, modulesComparingOnlyThemeScenarios: 1 })
assert.equal(clean.modules[0].comparesOnlyThemeScenarios, true, 'a module comparing only its theme scenario must be reported as such')

// Plant: a new surface ships without a visual-parity case and without a register row — the shape
// that let scheduler.month-agenda go uncompared.
const planted = catalogs()
planted[0].scenarios.push({ id: 'example.agenda-week', sourceQualityCaseIds: [] })
assert.match(galleryParityCoverage(planted, register()).errors.join('\n'), /not compared for visual parity and is not registered.*example\.agenda-week/)

// Plant: the register keeps a row for a surface that is now compared — a stale exemption.
const stale = register()
stale.modules['hlp.ui.example'].uncompared = ['example.month-agenda', 'example.theme-light']
assert.match(galleryParityCoverage(catalogs(), stale).errors.join('\n'), /registered uncompared scenario is compared.*example\.theme-light/)

// Plant: a register row with no reason, and a row for a module that does not exist.
const unreasoned = register()
delete unreasoned.modules['hlp.ui.example'].reason
assert.match(galleryParityCoverage(catalogs(), unreasoned).errors.join('\n'), /registered without a reason/)
assert.match(galleryParityCoverage(catalogs(), { modules: { ...register().modules, 'hlp.ui.ghost': { reason: 'x', uncompared: [] } } }).errors.join('\n'), /unknown module: hlp\.ui\.ghost/)

// The checked-in register must be exact against the real catalogs, so the gate cannot pass with a gap.
const root = resolve(import.meta.dirname, '../..')
const real = galleryParityCoverage(
  readdirSync(resolve(root, 'gallery/scenarios')).filter(name => name.endsWith('.json')).sort()
    .map(name => JSON.parse(readFileSync(resolve(root, 'gallery/scenarios', name), 'utf8'))),
  JSON.parse(readFileSync(resolve(root, 'gallery/visual-parity-coverage.json'), 'utf8')),
)
assert.deepEqual(real.errors, [])
assert.ok(real.totals.compared > 0 && real.totals.uncompared > 0)

// The known-divergence register the gallery tile rule reads: every row names a real scenario, a
// ticket, and per-host measurements — at least one of them above the frozen tile threshold (that is
// what makes it a divergence at all), each from a distinct named host, and each far enough above the
// measurement above the measured cross-engine noise ceiling (0.2656, the figure gallery.spec.ts
// calibrated the tile rule against) — a value inside the noise field is not a divergence and could
// never fail for being fixed. The gallery spec judges a run against ITS OWN host's row and fails
// loudly when the host has no row, so the register must never carry a catch-all host either.
// NOTE: the stricter bar (worstTilePixelRatio - tolerance > the ceiling, i.e. the whole +/-tolerance
// window clear of the noise field) now holds for all four remaining host entries (the lowest is
// scheduler.theme-light on windows-11-x64 at 0.4023, floor 0.3723). The row that failed it,
// side-nav.theme-dark on macos-x64-intel (0.2891, floor 0.2591), is gone: 282 train C reconciled the
// side-nav stylesheet with its authority and BOTH side-nav scenarios fell under the frozen tile
// threshold on every host (re-measured on train C: 0.1172 dark and 0 light on all three).
const divergences = JSON.parse(readFileSync(resolve(root, 'gallery/visual-parity-known-divergences.json'), 'utf8'))
const tileThreshold = JSON.parse(readFileSync(resolve(root, 'catalog/ui-theme-registry.json'), 'utf8')).visualParity.maximumChangedTilePixelRatio
const comparedIds = new Set(real.modules.flatMap(module => module.compared))
const measuredNoiseCeiling = 0.2656
const unknownHost = 'linux-x64'
for (const row of divergences.divergences) {
  assert.ok(comparedIds.has(row.scenarioId), `known divergence ${row.scenarioId} is not a compared scenario`)
  assert.ok(Array.isArray(row.measurements) && row.measurements.length > 0,
    `known divergence ${row.scenarioId} records no host measurement`)
  const hosts = row.measurements.map(entry => entry.host)
  assert.equal(new Set(hosts).size, hosts.length, `known divergence ${row.scenarioId} measures a host twice`)
  for (const entry of row.measurements) {
    assert.ok(typeof entry.host === 'string' && entry.host.length > 3, `known divergence ${row.scenarioId} has an unnamed host`)
    assert.ok(typeof entry.worstTilePixelRatio === 'number' && entry.worstTilePixelRatio > 0,
      `known divergence ${row.scenarioId} on ${entry.host} records no measured ratio`)
  }
  const ratios = row.measurements.map(entry => entry.worstTilePixelRatio)
  assert.ok(Math.max(...ratios) > tileThreshold, `known divergence ${row.scenarioId} does not exceed the tile threshold on any host`)
  for (const entry of row.measurements) {
    assert.ok(entry.worstTilePixelRatio > measuredNoiseCeiling,
      `known divergence ${row.scenarioId} on ${entry.host} sits ${entry.worstTilePixelRatio}, at or below the measured`
      + ` cross-engine noise ceiling ${measuredNoiseCeiling} — that is not a divergence, it is noise, and a row there`
      + ` could never fail for being fixed`)
  }
  // The spec looks the running host up by key; no row may answer for a host that never measured it.
  assert.equal(row.measurements.find(entry => entry.host === unknownHost), undefined,
    `known divergence ${row.scenarioId} answers for an unmeasured host (${unknownHost}); the spec must fail loudly instead`)
  assert.match(row.ticket, /^\d+$/)
  assert.ok(row.note.length > 10)
}

// A `resolved` entry naming a host lets the gallery spec hold THAT host to the frozen threshold
// instead of failing it for having no measurement (ticket 282: scheduler.theme-light on
// macos-x64-intel). Two ways that could lie: a host both measured and resolved on the same row, and
// a resolved host on a row that no longer exists at all (the whole-row removals carry no host).
const rowsById = new Map(divergences.divergences.map(row => [row.scenarioId, row]))
for (const entry of divergences.resolved ?? []) {
  assert.ok(entry.removedBy && entry.reason && entry.reason.length > 10,
    `resolved entry for ${entry.scenarioId} needs a removedBy ticket and a reason`)
  if (!entry.host) {
    assert.equal(rowsById.get(entry.scenarioId), undefined,
      `${entry.scenarioId} is resolved for every host but still carries a divergence row`)
    continue
  }
  const row = rowsById.get(entry.scenarioId)
  assert.ok(row, `${entry.scenarioId} is resolved on ${entry.host} but has no divergence row for the other hosts`)
  assert.equal(row.measurements.find(measurement => measurement.host === entry.host), undefined,
    `${entry.scenarioId} both measures and resolves ${entry.host}; a host is one or the other`)
}

process.stdout.write('gallery parity coverage check PASS\n')
