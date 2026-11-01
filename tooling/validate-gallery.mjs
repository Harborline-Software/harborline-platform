#!/usr/bin/env node

import { readFileSync, readdirSync, statSync } from 'node:fs'
import { resolve } from 'node:path'

import { galleryParityCoverage } from './gallery-parity-coverage.mjs'

const root = process.cwd()
const errors = []
const partialLegacyQualityCoverage = new Set(['hlp.ui.button', 'hlp.ui.context-menu'])
const definitions = readdirSync(resolve(root, 'gallery/scenarios'))
  .filter(name => name.endsWith('.json'))
  .sort()
  .map(name => {
    const catalog = load(`gallery/scenarios/${name}`)
    const storyName = catalog.moduleId.slice('hlp.ui.'.length)
      .split('-').map(part => part[0].toUpperCase() + part.slice(1)).join('')
    return {
      catalog: `gallery/scenarios/${name}`,
      surfaces: catalog.scenarios.map(scenario => scenario.surface),
      requireCompleteQualityCoverage: !partialLegacyQualityCoverage.has(catalog.moduleId),
      stories: {
        react: `gallery/projections/react/src/${storyName}.stories.tsx`,
        blazor: `gallery/projections/blazor/Stories/${storyName}.stories.razor`,
      },
    }
  })
const requiredFiles = [
  'gallery/README.md',
  'catalog/ui-theme-registry.json',
  ...definitions.flatMap(definition => [definition.catalog, ...Object.values(definition.stories)]),
  'gallery/projections/react/package.json',
  'gallery/projections/react/pnpm-lock.yaml',
  'gallery/projections/react/.storybook/main.ts',
  'gallery/projections/react/.storybook/preview.ts',
  'gallery/projections/blazor/Harborline.Gallery.Blazor.csproj',
  ...definitions.map(definition => definition.stories.blazor.replace('.stories.razor', 'Scenario.razor')),
  'gallery/projections/blazor/wwwroot/gallery.css',
  'gallery/tests/package.json',
  'gallery/tests/pnpm-lock.yaml',
  'gallery/tests/playwright.config.ts',
  'gallery/tests/tests/gallery.spec.ts',
  'gallery/aria-parity-known-divergences.json',
  'gallery/tab-walk-known-divergences.json',
  'gallery/tests/tests/tab-walk.ts',
  'tooling/prepare-galleries.mjs',
  'tooling/run-galleries.mjs',
  'tooling/run-gallery-gate.mjs',
]

function load(path) {
  return JSON.parse(readFileSync(resolve(root, path), 'utf8'))
}

function exists(path) {
  return statSync(resolve(root, path), { throwIfNoEntry: false })?.isFile() === true
}

// T-943: the ARIA-tree waiver register gallery.spec.ts reads. The spec judges each RUN scenario
// against its row (unwaived, changed and stale rows fail there); what no run can see is a row that
// names no scenario at all, or a row too thin to be reviewed, so those are refused here, statically.
// T-962's tab-walk register has the same review floor, so the row checks are shared and each register
// adds only what its rows record.
function laneRegisterErrors(register, scenarioIds, spec) {
  if (!Array.isArray(register?.divergences)) return [`${spec.path}: divergences must be an array`]
  const errors = []
  if (register.measureFlag !== spec.measureFlag) errors.push(`${spec.path}: measureFlag must be ${spec.measureFlag}`)
  errors.push(...(spec.registerErrors?.(register) ?? []))
  const known = new Set(scenarioIds)
  const seen = new Set()
  for (const [index, row] of register.divergences.entries()) {
    const label = `${spec.path}: waiver ${typeof row?.scenarioId === 'string' ? row.scenarioId : `#${index + 1}`}${spec.labelSuffix?.(row) ?? ''}`
    if (!row || typeof row !== 'object') {
      errors.push(`${label} is not an object`)
      continue
    }
    for (const key of Object.keys(row)) if (!spec.keys.includes(key)) errors.push(`${label} has an unknown field ${key}`)
    errors.push(...(spec.fieldErrors?.(row, label) ?? []))
    const duplicateKey = spec.duplicateKey(row)
    if (typeof row.scenarioId !== 'string' || !known.has(row.scenarioId)) errors.push(`${label} names an unknown scenario id`)
    else if (seen.has(duplicateKey)) errors.push(`${label} ${spec.duplicate(row)}`)
    seen.add(duplicateKey)
    const refused = spec.refusedTickets?.[row.ticket]
    if (typeof row.ticket !== 'string' || !/^T-\d+$/.test(row.ticket)) errors.push(`${label} needs the open ticket that owns the fix (T-NNN)`)
    else if (refused) errors.push(`${label} names ${row.ticket}, ${refused}; name the open ticket that owns the fix`)
    if (typeof row.reason !== 'string' || row.reason.trim().length < 20) errors.push(`${label} needs a reason a reviewer can check`)
    errors.push(...spec.lineErrors(row, label))
    errors.push(...(spec.rowErrors?.(row, label) ?? []))
  }
  return errors
}

// T-943 builds the comparison and closes when it lands, so a row naming it would point at a closed
// ticket the day it merges: every waiver names the open ticket that owns its fix (review 1, finding 2).
const t943 = { 'T-943': 'the ticket that builds this register and closes with it' }

// Hunks as laneHunks writes them: '@@ #k' opens each hunk (k anchors it among repeated lines), then
// context ('  '), React-only ('- ') and Blazor-only ('+ ') markers, each followed by a measured line.
// Anything else was not measured.
function hunkErrors(hunks, label, { lineShape, noun }) {
  const header = line => /^@@ #\d+$/.test(line)
  if (!Array.isArray(hunks) || hunks.some(line => typeof line !== 'string' || !(header(line) || lineShape.test(line)))) {
    return [`${label} must record hunks as laneHunks writes them ('@@ #k', then '  ', '- ' or '+ ' lines)`]
  }
  if (!header(hunks[0] ?? '') || !hunks.some(line => line.startsWith('- ') || line.startsWith('+ '))) {
    return [`${label} records no differing ${noun}, so it waives nothing a run can measure`]
  }
  return []
}

const ariaRegisterPath = 'gallery/aria-parity-known-divergences.json'
const ariaWaiverKeys = ['scenarioId', 'state', 'ticket', 'reason', 'hunks']
export function ariaParityRegisterErrors(register, scenarioIds) {
  return laneRegisterErrors(register, scenarioIds, {
    path: ariaRegisterPath,
    measureFlag: 'HARBORLINE_GALLERY_ARIA_MEASURE',
    keys: ariaWaiverKeys,
    // `state` names a later moment of a timer-driven scene, judged apart from the prepared scene (the
    // row without one); gallery.spec.ts refuses a state the scenario never reads.
    fieldErrors: (row, label) => 'state' in row && (typeof row.state !== 'string' || !/^[a-z]+(?:-[a-z]+)*$/.test(row.state))
      ? [`${label} has a state that is not a kebab-case name`] : [],
    duplicateKey: row => `${row.scenarioId} @${row.state ?? ''}`,
    duplicate: row => `is registered twice${row.state ? ` for state ${row.state}` : ''}; one row records the whole difference`,
    refusedTickets: t943,
    // A snapshot line always starts, after its indentation, with '- '.
    lineErrors: (row, label) => hunkErrors(row.hunks, label, { lineShape: /^[ +-] \s*- /, noun: 'snapshot line' }),
  })
}

// T-962: the tab-walk register. Every catalog scenario is walked, so every catalog id is a valid
// row target. An `order` row records the cross-lane diff of the two lanes' order lines as hunks
// (position-anchored, like T-943's; never host-scoped); a `findings` row records each lane's complete
// finding lines, optionally for one known host. A scenario has either one host-less findings row or
// host-scoped ones, never both, and every finding line names its WCAG clause and walk direction, which
// is what the spec's per-clause messages split on.
const tabWalkRegisterPath = 'gallery/tab-walk-known-divergences.json'
const tabWalkRowKeys = ['scenarioId', 'kind', 'host', 'ticket', 'reason', 'hunks', 'reactOnly', 'blazorOnly', 'presses', 'pressMs']
// A wall-clock deadline means the walk did not finish inside the test's allowance: how far it got
// depends on the host and its load, so no row may waive one. A lane too slow to walk in full gets a
// budget row instead, which stops every walk after a fixed number of presses.
const tabWalkDeadline = /^2\.1\.2 (?:forward|reverse|prepared|walk) deadline:|^[ +-] deadline$/
const tabWalkHosts = ['windows-11-x64', 'linux-x64', 'macos-x64-intel', 'macos-arm64']
const tabWalkFindingLine = /^(?:2\.1\.2|2\.4\.3|2\.4\.11) (?:forward|reverse|prepared|walk) \S/
export function tabWalkRegisterErrors(register, scenarioIds) {
  const findingsHosts = new Map()
  for (const row of Array.isArray(register?.divergences) ? register.divergences : []) {
    if (row?.kind !== 'findings' || typeof row.scenarioId !== 'string') continue
    findingsHosts.set(row.scenarioId, [...(findingsHosts.get(row.scenarioId) ?? []), row.host])
  }
  return laneRegisterErrors(register, scenarioIds, {
    path: tabWalkRegisterPath,
    measureFlag: 'HARBORLINE_GALLERY_TAB_WALK_MEASURE',
    keys: tabWalkRowKeys,
    registerErrors: candidate => typeof candidate.rule === 'string' && candidate.rule.trim().length > 0 ? [] : [`${tabWalkRegisterPath}: rule must state the register's rule`],
    labelSuffix: row => typeof row?.kind === 'string' ? ` ${row.kind}${typeof row.host === 'string' ? ` ${row.host}` : ''}` : '',
    duplicateKey: row => `${row.scenarioId}\t${row.kind}\t${row.host ?? ''}`,
    duplicate: () => 'is registered twice for the same kind and host',
    // The gate's own ticket is refused here as T-943 is in its register: T-962 builds this register and
    // closes with it, so every row names the open ticket that owns its fix.
    refusedTickets: {
      'T-962': 'the ticket that builds this register and closes with it',
      'T-943': 'the ticket that builds the ARIA-tree register, not a fix',
    },
    lineErrors: (row, label) => {
      if (row.kind === 'budget') {
        const errors = []
        if (row.hunks !== undefined || row.reactOnly !== undefined || row.blazorOnly !== undefined) errors.push(`${label} is a budget row; it records presses and pressMs, not lines`)
        if (!Number.isInteger(row.presses) || row.presses < 8 || row.presses > 200) errors.push(`${label} must give presses, a whole number of Tab presses from 8 to 200`)
        if (!Number.isInteger(row.pressMs) || row.pressMs < 45 || row.pressMs > 2000) errors.push(`${label} must give pressMs, the measured cost of one press in milliseconds, from 45 to 2000`)
        return errors
      }
      if (row.presses !== undefined || row.pressMs !== undefined) return [`${label} records presses or pressMs, which only a budget row takes`]
      if (row.kind === 'order') {
        // Order lines are walk lines (`- <key>`, `↻ - <key>`, `↺ ok`, `released by …`, `exited` and
        // the outcome words), so any non-blank text may follow the marker.
        return [
          ...hunkErrors(row.hunks, label, { lineShape: /^[ +-] \S/, noun: 'walk line' }),
          ...(row.reactOnly !== undefined || row.blazorOnly !== undefined ? [`${label} is an order row; it records hunks, not reactOnly or blazorOnly`] : []),
          ...(Array.isArray(row.hunks) && row.hunks.some(line => typeof line === 'string' && tabWalkDeadline.test(line))
            ? [`${label} waives a wall-clock deadline, which depends on the host; give the scenario a budget row instead`] : []),
        ]
      }
      if (row.kind !== 'findings') return []
      const lines = ['reactOnly', 'blazorOnly'].map(lane => row[lane])
      if (row.hunks !== undefined) return [`${label} is a findings row; it records reactOnly and blazorOnly, not hunks`]
      if (lines.some(lane => !Array.isArray(lane) || lane.some(line => typeof line !== 'string' || line.trim().length === 0))) {
        return [`${label} must record reactOnly and blazorOnly as arrays of finding lines`]
      }
      if (lines.every(lane => lane.length === 0)) return [`${label} records no finding line, so it waives nothing a run can measure`]
      return [
        ...[...row.reactOnly, ...row.blazorOnly].filter(line => !tabWalkFindingLine.test(line))
          .map(line => `${label} has a finding line that does not start with its clause and direction: ${JSON.stringify(line)}`),
        ...([...row.reactOnly, ...row.blazorOnly].some(line => tabWalkDeadline.test(line))
          ? [`${label} waives a wall-clock deadline, which depends on the host; give the scenario a budget row instead`] : []),
      ]
    },
    rowErrors: (row, label) => {
      const errors = []
      if (!['order', 'findings', 'budget'].includes(row.kind)) errors.push(`${label} has an unknown kind; it is order, findings or budget`)
      if ((row.kind === 'order' || row.kind === 'budget') && row.host !== undefined) errors.push(`${label} is ${row.kind === 'order' ? 'an order' : 'a budget'} row, which applies on every host; it takes no host`)
      if (row.host !== undefined && !tabWalkHosts.includes(row.host)) errors.push(`${label} names an unknown host`)
      const hosts = findingsHosts.get(row.scenarioId) ?? []
      if (row.kind === 'findings' && row.host === undefined && hosts.some(host => host !== undefined)) {
        errors.push(`${label} mixes a host-less findings row with host-scoped ones; use one or the other`)
      }
      return errors
    },
  })
}

const summaries = []
const allScenarioIds = []
for (const definition of definitions) {
  const catalog = load(definition.catalog)
  const conformance = load(catalog.sourceFixture)
  const conformanceIds = new Set(conformance.cases.map(entry => entry.id))
  const quality = load(catalog.qualityFixture)
  const qualityIds = new Set([
    ...quality.accessibilityCases.map(entry => entry.id),
    ...quality.internationalizationCases.map(entry => entry.id),
    ...quality.themingCases.map(entry => entry.id),
    ...(quality.dismissalCases ?? []).map(entry => entry.id),
  ])
  const scenarios = catalog.scenarios ?? []
  const ids = scenarios.map(entry => entry.id)
  allScenarioIds.push(...ids)
  const surfaces = scenarios.map(entry => entry.surface)
  const prefix = catalog.moduleId

  if (catalog.moduleId !== conformance.moduleId) errors.push(`${prefix}: gallery module differs from conformance module`)
  if (catalog.moduleId !== quality.moduleId) errors.push(`${prefix}: gallery module differs from quality fixture module`)
  if (new Set(ids).size !== ids.length) errors.push(`${prefix}: duplicate gallery scenario id`)
  if (JSON.stringify([...surfaces].sort()) !== JSON.stringify([...definition.surfaces].sort())) {
    errors.push(`${prefix}: gallery surfaces differ from the required set`)
  }
  for (const theme of quality.themes ?? []) {
    const scenario = scenarios.find(entry => entry.id === theme.scenarioId)
    if (!scenario) errors.push(`${theme.id}: missing separate theme scenario ${theme.scenarioId}`)
    if (scenario && scenario.surface !== `theme-${theme.id}`) errors.push(`${theme.id}: theme scenario has the wrong surface`)
  }
  for (const scenario of scenarios) {
    if (!scenario.sourceCaseIds?.length) errors.push(`${scenario.id}: no conformance source case`)
    for (const caseId of scenario.sourceCaseIds ?? []) {
      if (!conformanceIds.has(caseId)) errors.push(`${scenario.id}: unknown conformance source ${caseId}`)
    }
    for (const caseId of scenario.sourceQualityCaseIds ?? []) {
      if (!qualityIds.has(caseId)) errors.push(`${scenario.id}: unknown quality source ${caseId}`)
    }
  }
  if (definition.requireCompleteQualityCoverage) {
    const coveredQualityIds = new Set(scenarios.flatMap(scenario => scenario.sourceQualityCaseIds ?? []))
    for (const caseId of qualityIds) {
      if (!coveredQualityIds.has(caseId)) errors.push(`${prefix}: quality fixture case is not projected by a gallery scenario: ${caseId}`)
    }
  }
  for (const [projection, path] of Object.entries(definition.stories)) {
    if (!exists(path)) continue
    const source = readFileSync(resolve(root, path), 'utf8')
    for (const scenario of scenarios) {
      if (!source.includes(scenario.id)) errors.push(`${projection}: missing scenario ${scenario.id}`)
    }
    if (projection === 'react' && !source.includes("from '@harborline-software/ui-react'")) {
      errors.push(`${projection}: ${prefix} stories do not import the public package identity`)
    }
    if (/projections\/(?:react|blazor)\/ui/.test(source)) errors.push(`${projection}: ${prefix} implementation path import`)
  }
  summaries.push({moduleId: catalog.moduleId, scenarios: scenarios.length, sourceCases: conformanceIds.size, qualityCases: qualityIds.size})
}

for (const path of requiredFiles) if (!exists(path)) errors.push(`missing ${path}`)

if (exists(ariaRegisterPath)) {
  errors.push(...ariaParityRegisterErrors(load(ariaRegisterPath), allScenarioIds))
  // A register nothing reads is a waiver list that waives nothing and checks nothing.
  const tests = exists('gallery/tests/tests/gallery.spec.ts') ? readFileSync(resolve(root, 'gallery/tests/tests/gallery.spec.ts'), 'utf8') : ''
  if (!tests.includes(ariaRegisterPath) || !tests.includes('await assertAriaParity(reactPage, blazorPage, scenario.id)')) {
    errors.push('gallery tests do not compare every scenario against the ARIA parity register')
  }
}
if (exists(tabWalkRegisterPath)) {
  errors.push(...tabWalkRegisterErrors(load(tabWalkRegisterPath), allScenarioIds))
  const tests = exists('gallery/tests/tests/gallery.spec.ts') ? readFileSync(resolve(root, 'gallery/tests/tests/gallery.spec.ts'), 'utf8') : ''
  if (!tests.includes(tabWalkRegisterPath) || !tests.includes('await assertTabWalk(reactPage, blazorPage, scenario')) {
    errors.push('gallery tests do not walk every scenario against the tab-walk register')
  }
}

const rootManifest = load('package.json')
for (const script of ['gallery:react', 'gallery:blazor', 'gallery', 'test:gallery']) {
  if (!rootManifest.scripts?.[script]) errors.push(`missing root script ${script}`)
}

if (exists('gallery/projections/react/package.json')) {
  const react = load('gallery/projections/react/package.json')
  if (react.private !== true) errors.push('React gallery must be private')
  if (react.dependencies?.['@harborline-software/ui-react']) {
    errors.push('React gallery package manifest must not resolve the candidate from a registry or source path')
  }
}
if (exists('gallery/tests/package.json') && load('gallery/tests/package.json').private !== true) {
  errors.push('Gallery test harness must be private')
}
if (exists('gallery/projections/blazor/Harborline.Gallery.Blazor.csproj')) {
  const project = readFileSync(resolve(root, 'gallery/projections/blazor/Harborline.Gallery.Blazor.csproj'), 'utf8')
  if (!/<IsPackable>\s*false\s*<\/IsPackable>/.test(project)) errors.push('Blazor gallery must be non-packable')
  if (/<ProjectReference\b/.test(project)) errors.push('Blazor gallery must consume packed NuGet artifacts')
  if (!/<PackageReference Include="Harborline.UIAdapters.Blazor"/.test(project)) errors.push('Blazor gallery does not reference the public NuGet identity')
}
for (const path of ['gallery/projections/blazor/wwwroot/index.html', 'gallery/projections/blazor/wwwroot/iframe.html']) {
  if (!exists(path)) continue
  const source = readFileSync(resolve(root, path), 'utf8')
  if (!source.includes('_content/Harborline.UIAdapters.Blazor/feedback.css')) {
    errors.push(`${path}: feedback modules do not load CSS from the packed aggregate artifact`)
  }
  if (!source.includes('_content/Harborline.UIAdapters.Blazor/empty-state.css')) {
    errors.push(`${path}: Empty State does not load CSS from the packed aggregate artifact`)
  }
  if (!source.includes('_content/Harborline.UIAdapters.Blazor/rule-authoring.css')) {
    errors.push(`${path}: Rule Authoring does not load CSS from the packed aggregate artifact`)
  }
}
if (exists('catalog/ui-theme-registry.json') && exists('gallery/styles/canvas.css')) {
  const registry = load('catalog/ui-theme-registry.json')
  const canvas = readFileSync(resolve(root, 'gallery/styles/canvas.css'), 'utf8')
  const buttonQuality = load('conformance/hlp.ui.button/quality-fixtures.yaml')
  const expectedAuthority = {
    repository: 'shipyard',
    commit: 'a5036ff8b500e0d857d7e0a698408e7cf7f6cf31',
    path: 'packages/ui-react/src/style.css',
    blob: 'bca2eec4f4e90e5fd9e1ab3d94dc24317ca20860',
  }
  if (registry.schemaVersion !== 1 || JSON.stringify(registry.authority) !== JSON.stringify(expectedAuthority)) {
    errors.push('theme registry is not pinned to the exact upstream theme source blob')
  }
  if (registry.visualParity?.pixelmatchThreshold !== 0.15
      || registry.visualParity?.maximumChangedPixelRatio !== 0.015
      || registry.visualParity?.includeAntialiasing !== false
      || registry.visualParity?.tileSize !== 16
      || registry.visualParity?.maximumChangedTilePixelRatio !== 0.34) {
    errors.push('theme registry visual parity thresholds changed without a registry revision')
  }
  for (const [token, values] of Object.entries(registry.publicTokens ?? {})) {
    if (!canvas.includes(`${token}: ${values.light};`)) errors.push(`theme registry light token missing from canvas: ${token}`)
    if (!canvas.includes(`${token}: ${values.dark};`)) errors.push(`theme registry dark token missing from canvas: ${token}`)
  }
  for (const [alias, target] of Object.entries(registry.galleryAliases ?? {})) {
    if (!canvas.includes(`${alias}: var(${target});`)) errors.push(`theme registry alias missing from canvas: ${alias}`)
    for (const theme of buttonQuality.themes ?? []) {
      const expected = registry.publicTokens?.[target]?.[theme.id]
      if (expected && theme.tokens?.[alias] !== expected) errors.push(`${theme.id}: ${alias} differs from pinned upstream token ${target}`)
    }
  }
  if (exists('gallery/tests/tests/gallery.spec.ts')) {
    const tests = readFileSync(resolve(root, 'gallery/tests/tests/gallery.spec.ts'), 'utf8')
    if (!tests.includes("catalog/ui-theme-registry.json")
        || !tests.includes('themeRegistry.visualParity.pixelmatchThreshold')
        || !tests.includes('themeRegistry.visualParity.maximumChangedPixelRatio')
        || !tests.includes('themeRegistry.visualParity.maximumChangedTilePixelRatio')) {
      errors.push('gallery visual parity tests do not consume the frozen theme registry')
    }
  }
}
if (exists('gallery/projections/blazor/wwwroot/gallery.css')) {
  const sharedCanvas = readFileSync(resolve(root, 'gallery/styles/canvas.css'), 'utf8')
  const blazorCanvas = readFileSync(resolve(root, 'gallery/projections/blazor/wwwroot/gallery.css'), 'utf8')
  if (sharedCanvas !== blazorCanvas) errors.push('React and Blazor galleries must use the same canvas stylesheet')
}
if (exists('tooling/prepare-galleries.mjs')) {
  const preparation = readFileSync(resolve(root, 'tooling/prepare-galleries.mjs'), 'utf8')
  if (!preparation.includes('harborline-software-contracts-0.0.0-alpha.0.tgz')) {
    errors.push('React gallery preparation does not install the required packed contracts peer')
  }
  if (!/contractsNpmArtifact, npmArtifact/.test(preparation)) {
    errors.push('React gallery preparation must install packed contracts and UI aggregate artifacts together')
  }
}

for (const path of requiredFiles.filter(path => /(?:package\.json|\.csproj|\.tsx|\.razor|\.mjs)$/.test(path))) {
  if (!exists(path)) continue
  const source = readFileSync(resolve(root, path), 'utf8')
  if (/\b(?:Fleet|ordinance|Anchor|Bridge|anchor-mobile-ios|accelerators?)\b/i.test(source)) errors.push(`${path}: prohibited gallery dependency or source reference`)
  if (/(?:file|link|portal|workspace):/.test(source)) errors.push(`${path}: local package protocol`)
}

// Parity coverage is reported per surface, not per module: a module that compares only its theme
// scenarios is visible as such in `parityCoverage`, and every uncompared surface is held against an
// exact register (control ticket 147).
const parityCoverage = galleryParityCoverage(
  readdirSync(resolve(root, 'gallery/scenarios')).filter(name => name.endsWith('.json')).sort()
    .map(name => load(`gallery/scenarios/${name}`)),
  exists('gallery/visual-parity-coverage.json') ? load('gallery/visual-parity-coverage.json') : {modules: {}},
)
errors.push(...parityCoverage.errors)

const report = {
  schemaVersion: 2,
  moduleIds: summaries.map(summary => summary.moduleId),
  status: errors.length ? 'FAIL' : 'PASS',
  counts: {
    modules: summaries.length,
    scenarios: summaries.reduce((total, summary) => total + summary.scenarios, 0),
    sourceCases: summaries.reduce((total, summary) => total + summary.sourceCases, 0),
    qualityCases: summaries.reduce((total, summary) => total + summary.qualityCases, 0),
    projections: 2,
  },
  modules: summaries,
  parityCoverage: {totals: parityCoverage.totals, modules: parityCoverage.modules},
  checks: {privateApplications: errors.some(error => error.includes('private')) ? null : 2, publishableGalleryArtifacts: 0, implementationInternalImports: 0},
  errors,
}

// Callable in-process by validate-repository.mjs, which used to spawn this file as a child and
// JSON-parse its stdout - same language, same process, plus a "did not emit JSON" error path that
// could only fire if the child crashed (ticket 099 item 7). Still a CLI when run directly.
export function validateGallery() {
  return report
}

if (import.meta.main) {
  process.stdout.write(`${JSON.stringify(report, null, 2)}\n`)
  process.exitCode = errors.length ? 1 : 0
}
