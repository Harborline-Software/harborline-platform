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
  'gallery/projections/react/package-lock.json',
  'gallery/projections/react/.storybook/main.ts',
  'gallery/projections/react/.storybook/preview.ts',
  'gallery/projections/blazor/Harborline.Gallery.Blazor.csproj',
  ...definitions.map(definition => definition.stories.blazor.replace('.stories.razor', 'Scenario.razor')),
  'gallery/projections/blazor/wwwroot/gallery.css',
  'gallery/tests/package.json',
  'gallery/tests/package-lock.json',
  'gallery/tests/playwright.config.ts',
  'gallery/tests/tests/gallery.spec.ts',
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

const summaries = []
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
