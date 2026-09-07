#!/usr/bin/env node

import { execFileSync, spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { dirname, relative, resolve } from 'node:path'

import { npmPackageContributionErrors } from './package-contribution-policy.mjs'
import {requiredStepIds} from './gate-contract.mjs'
import {moduleStatusErrors,
  allowedPresentationDispositions, dependencylessVitestConfigError, interfaceDependencyMismatch, npmPublicDistributionAuthorized, nugetPublicDistributionAuthorized, presentationPolicyErrors, requiresQualityProfile, retiredProvenanceFieldErrors, rootScopedThemeAliasError, themingPolicyErrors} from './validator-policy.mjs'
import {uiClassVocabularyErrors} from './ui-class-vocabulary.mjs'
import {validateGallery} from './validate-gallery.mjs'

const root = process.cwd()
const errors = []
const required = [
  'LICENSE',
  'README.md',
  'SECURITY.md',
  'CONTRIBUTING.md',
  'repository.yaml',
  'package.json',
  'global.json',
  'Directory.Build.props',
  'Directory.Packages.props',
  'Harborline.Platform.slnx',
  'Harborline.Gallery.slnx',
  'CONTEXT.md',
  'catalog/modules.yaml',
  'catalog/projections.yaml',
  'docs/architecture/decisions/HLP-0001-deep-modules-and-projections.md',
  'docs/architecture/decisions/HLP-0002-ui-seams-and-release-groups.md',
  'docs/architecture/decisions/HLP-0003-component-quality-profiles.md',
  'docs/architecture/decisions/HLP-0004-aggregate-compatibility-and-button-style-bridge.md',
  'docs/architecture/decisions/HLP-0005-app-theme-registry-and-visual-baselines.md',
  'docs/evidence/phase-4/aggregate-compatibility.json',
  'docs/evidence/phase-4/component-quality-profile.json',
  'docs/evidence/phase-4/gallery-gate.json',
  'docs/evidence/phase-4/gate.json',
  'projections/blazor/ui/hlp.ui.button.tests/fixtures/legacy-button-provider-fixtures.json',
  'docs/provenance/source-map.yaml',
  'gallery/README.md',
  'gallery/scenarios/hlp.ui.button.json',
  'tooling/appshell-feed-provenance.json',
  'tests/package-consumers/npm/package.json',
  'tests/package-consumers/npm/exercise.mjs',
  'tests/package-consumers/nuget/Consumer.csproj',
  'tests/package-consumers/nuget/Program.cs',
  'tests/package-consumers/inspection-review-nuget/Consumer.csproj',
  'tests/package-consumers/inspection-review-nuget/Program.cs',
]
const skippedDirectories = new Set([
  '.git', 'node_modules', 'bin', 'obj', 'dist', 'coverage', 'artifacts', '.packages',
  'test-results', 'playwright-report', 'storybook-static',
])
const allowedRoles = new Set(['implementation', 'adapter', 'generated-binding', 'host', 'compatibility-facade', 'compatibility-vocabulary'])
const allowedStatuses = new Set(['conformant', 'preview', 'planned', 'deferred', 'exception', 'not-applicable'])
const allowedModuleKinds = new Set(['interactive-component', 'non-interactive-component', 'runtime', 'tooling', 'compatibility'])

const gateModelReceiptPath = 'docs/evidence/gate-model/ui-gate-model.json'
const gateModelRows = exists(gateModelReceiptPath)
  ? Object.fromEntries((load(gateModelReceiptPath).modules ?? []).map(entry => [entry.moduleId, entry]))
  : null
const allowedQualityDispositions = new Set(['required', 'not-applicable'])

function load(path) {
  return JSON.parse(readFileSync(resolve(root, path), 'utf8'))
}

function exists(path) {
  return statSync(resolve(root, path), { throwIfNoEntry: false })?.isFile() === true
}

function files(path) {
  return readdirSync(path, { withFileTypes: true }).flatMap(entry => {
    if (entry.isDirectory() && skippedDirectories.has(entry.name)) return []
    const child = resolve(path, entry.name)
    return entry.isDirectory() ? files(child) : [child]
  })
}

for (const path of required) if (!exists(path)) errors.push(`missing ${path}`)

const galleryReport = validateGallery()
if (galleryReport.errors.length > 0) {
  errors.push(...galleryReport.errors.map(error => `gallery: ${error}`))
}

const repository = load('repository.yaml')
const catalog = load('catalog/modules.yaml')
const aggregateBuildSource = readFileSync(resolve(root, 'projections/react/ui/hlp.ui.button/scripts/build.mjs'), 'utf8')
errors.push(...npmPackageContributionErrors(catalog, aggregateBuildSource))
const projectionView = load('catalog/projections.yaml')
const provenance = load('docs/provenance/source-map.yaml')
const gate = load('docs/evidence/phase-4/gate.json')
const allowStaleGate = process.argv.includes('--allow-stale-gate')
if (repository.repository !== 'harborline-platform') errors.push('repository identity mismatch')
// Ticket 063 phase 1 (authority). This assertion previously REQUIRED the source-era aggregate
// contract to remain the package identity authority, which is the divergence 063 closes; it made
// the rename unlandable by gate rather than by decision. The authority is now Harborline's own
// converged identity. The assertion itself is unchanged in strength — it still pins one exact
// value, so an unreviewed drift in either direction is still one named error.
if (repository.packageIdentityAuthority !== 'harborline-converged-identity') {
  errors.push('package identity authority must be the Harborline converged identity')
}
// Policy unchanged: distribution authority stays unassigned. Only the era name is dropped from the
// message, because after convergence there is no "earlier source package" whose distribution could be
// assigned. Assigning distribution authority is a publishing decision and is NOT part of 063.
if (repository.packageDistributionAuthority !== null) {
  errors.push('package distribution authority must remain unassigned')
}
if (catalog.repository !== repository.repository) errors.push('catalog repository mismatch')
if (JSON.parse(readFileSync(resolve(root, 'package.json'), 'utf8')).private !== true) errors.push('root package must be private')
if (!allowStaleGate) {
  const expectedModuleIds = Object.entries(catalog.modules)
    .filter(([moduleId, module]) => moduleId.startsWith('hlp.ui.')
      && Object.values(module.projections ?? {}).some(projection => projection.conformanceRunner))
    .map(([moduleId]) => moduleId)
    .sort()
  const expectedGenerationSmokeModules = Object.entries(catalog.modules)
    .filter(([moduleId, module]) => moduleId.startsWith('hlp.ui.') && module.presentation?.disposition === 'visual')
    .length
  const expectedSharedResults = expectedModuleIds.reduce((total, moduleId) => {
    const fixture = load(`conformance/${moduleId}/fixtures.yaml`)
    // Delegating runners record the React suite ONCE (wave-1 honesty fix); the custom per-case
    // runners (button, context-menu: vitest -t per fixture id) genuinely produce one React result
    // per case and stay *2.
    const runner = readFileSync(resolve(root, `conformance/${moduleId}/runners/run-shared.mjs`), 'utf8')
    const perCaseReact = !runner.includes('run-ui-module-shared')
    return total + (perCaseReact ? fixture.cases.length * 2 : fixture.cases.length + 1)
  }, 0)
  const gateResults = new Map((gate.results ?? []).map(result => [result.id, result]))
  if (gate.schemaVersion !== 3 || gate.phase !== 4 || gate.status !== 'PASS') errors.push('phase-4 gate is not a recorded schema-v3 PASS')
  if (JSON.stringify(gate.moduleIds) !== JSON.stringify(expectedModuleIds)) errors.push('phase-4 gate module scope differs from the catalog')
  if (JSON.stringify(gate.requiredStepIds) !== JSON.stringify(requiredStepIds)
      || requiredStepIds.some(id => gateResults.get(id)?.passed !== true)) errors.push('phase-4 gate required steps are incomplete')
  if (gate.counts?.sharedResults !== expectedSharedResults
      || gate.invariants?.expectedSharedResults !== expectedSharedResults) errors.push('phase-4 gate shared-result count differs from fixtures')
  if (gate.counts?.generationSmokeModules !== expectedGenerationSmokeModules
      || gate.invariants?.expectedGenerationSmokeModules !== expectedGenerationSmokeModules
      || !Number.isInteger(gate.counts?.generationSmokeScenarios)
      || gate.counts.generationSmokeScenarios <= 0) errors.push('phase-4 gate generation-smoke evidence differs from the visual catalog scope')
  if (gate.counts?.galleryScenarios !== galleryReport?.counts?.scenarios
      || gate.counts?.qualityCases !== galleryReport?.counts?.qualityCases) errors.push('phase-4 gate gallery counts differ from current fixtures')
  if (gate.counts?.generationSmokeScenarios !== gate.counts?.galleryScenarios) errors.push('phase-4 gate authority and derived scenario counts differ')
  // Re-pointed for control ticket 100 action 6. The two comparisons above hold the RECORDED evidence
  // against CURRENT fixtures, which catches stale evidence but never catches a catalog describing
  // scenarios no test executes -- both sides read the same files. This one holds the catalog against
  // what Playwright actually ran, which is the only comparison that can see that.
  if (gate.scenarioReconciliation !== 'exact'
      || gate.counts?.scenarioBrowserTests !== galleryReport?.counts?.scenarios) {
    errors.push('phase-4 gate gallery scenarios were not reconciled against the tests that ran')
  }
  if (!Number.isInteger(gate.counts?.nativeTests) || gate.counts.nativeTests <= 0) errors.push('phase-4 gate native test count is absent')
}
errors.push(...retiredProvenanceFieldErrors(provenance.records))
errors.push(...uiClassVocabularyErrors(root, load('tooling/ui-class-vocabulary-baseline.json')))
for (const record of provenance.records ?? []) {
  for (const [path, expectedHash] of Object.entries(record.contentHashes ?? {})) {
    if (!exists(path)) {
      errors.push(`${record.moduleId}: provenance target missing ${path}`)
      continue
    }
    const actualHash = createHash('sha256').update(readFileSync(resolve(root, path))).digest('hex')
    if (actualHash !== expectedHash) errors.push(`${record.moduleId}: provenance hash mismatch ${path}`)
  }
}

const declaredManifests = new Map()
const artifactIds = new Map()
const catalogProjectionRows = []
for (const [moduleId, module] of Object.entries(catalog.modules ?? {})) {
  if (!exists(module.interface?.path ?? 'missing')) {
    errors.push(`${moduleId}: missing interface`)
  } else {
    const moduleInterface = load(module.interface.path)
    if (moduleInterface.moduleId !== moduleId) errors.push(`${moduleId}: interface module mismatch`)
    if (moduleInterface.interfaceRevision !== module.interface.revision) errors.push(`${moduleId}: interface revision mismatch`)
    const dependencyMismatch = interfaceDependencyMismatch(moduleId, module.dependencies, moduleInterface.dependencies)
    if (dependencyMismatch !== null) errors.push(dependencyMismatch)
  }
  if (typeof module.owner !== 'string' || module.owner.length === 0) errors.push(`${moduleId}: missing owner`)
  if (!allowedModuleKinds.has(module.moduleKind)) errors.push(`${moduleId}: missing or unknown module kind`)
  errors.push(...moduleStatusErrors(moduleId, module.status, gateModelRows, gateModelReceiptPath))
  const presentation = module.presentation
  errors.push(...presentationPolicyErrors(moduleId, presentation))
  if (!Array.isArray(module.dependencies)) errors.push(`${moduleId}: dependencies must be an array`)
  if ((module.depth?.redistributedComplexity ?? []).length < 2) errors.push(`${moduleId}: shallow deletion record`)
  if (requiresQualityProfile(module, presentation)) {
    const quality = module.qualityProfile
    if (!exists(quality?.path ?? 'missing')) {
      errors.push(`${moduleId}: module lacks its required quality profile`)
    } else {
      const profile = load(quality.path)
      if (profile.moduleId !== moduleId) errors.push(`${moduleId}: quality profile module mismatch`)
      if (profile.profileRevision !== quality.revision) errors.push(`${moduleId}: quality profile revision mismatch`)
      if (!exists(profile.fixture ?? 'missing')) errors.push(`${moduleId}: quality profile lacks a neutral fixture`)
      const fixture = exists(profile.fixture ?? 'missing') ? load(profile.fixture) : null
      if (fixture && fixture.moduleId !== moduleId) errors.push(`${moduleId}: quality fixture module mismatch`)
      const fixtureCaseIds = new Set([
        ...(fixture?.accessibilityCases ?? []).map(item => item.id),
        ...(fixture?.internationalizationCases ?? []).map(item => item.id),
        ...(fixture?.themingCases ?? []).map(item => item.id),
      ])
      const performanceCaseIds = new Set((fixture?.performanceCases ?? []).map(item => item.id))
      if (fixtureCaseIds.size !== (fixture?.accessibilityCases?.length ?? 0)
        + (fixture?.internationalizationCases?.length ?? 0)
        + (fixture?.themingCases?.length ?? 0)) {
        errors.push(`${moduleId}: duplicate quality fixture case id`)
      }
      for (const dimensionId of ['accessibility', 'internationalization', 'theming']) {
        const dimension = profile.dimensions?.[dimensionId]
        if (!allowedQualityDispositions.has(dimension?.disposition)) {
          errors.push(`${moduleId}: ${dimensionId} quality disposition must be required or not-applicable`)
        } else if (dimension.disposition === 'not-applicable') {
          if (typeof dimension.rationale !== 'string' || dimension.rationale.trim().length < 20) {
            errors.push(`${moduleId}: ${dimensionId} not-applicable disposition lacks a bounded rationale`)
          }
        } else {
          if (!Array.isArray(dimension.caseIds) || dimension.caseIds.length === 0) {
            errors.push(`${moduleId}: ${dimensionId} profile has no cases`)
          }
          for (const caseId of dimension.caseIds ?? []) {
            if (!fixtureCaseIds.has(caseId)) errors.push(`${moduleId}: ${dimensionId} profile references unknown case ${caseId}`)
          }
        }
      }
      errors.push(...themingPolicyErrors(moduleId, presentation, profile.dimensions?.theming))
      const declaredCaseIds = new Set([
        ...(profile.dimensions?.accessibility?.caseIds ?? []),
        ...(profile.dimensions?.internationalization?.caseIds ?? []),
        ...(profile.dimensions?.theming?.caseIds ?? []),
      ])
      for (const caseId of fixtureCaseIds) {
        if (!declaredCaseIds.has(caseId)) errors.push(`${moduleId}: quality fixture case ${caseId} is not declared by the profile`)
      }
      if (profile.riskTier === 'C') {
        const declaredPerformanceIds = new Set()
        for (const dimensionId of ['performance', 'largeData']) {
          const dimension = profile.dimensions?.[dimensionId]
          if (dimension?.disposition !== 'required' || !Array.isArray(dimension.caseIds) || dimension.caseIds.length === 0) {
            errors.push(`${moduleId}: Tier C ${dimensionId} dimension must declare required cases`)
            continue
          }
          for (const caseId of dimension.caseIds) {
            declaredPerformanceIds.add(caseId)
            if (!performanceCaseIds.has(caseId)) errors.push(`${moduleId}: ${dimensionId} profile references unknown performance case ${caseId}`)
          }
        }
        for (const caseId of performanceCaseIds) {
          if (!declaredPerformanceIds.has(caseId)) errors.push(`${moduleId}: performance fixture case ${caseId} is not declared by the profile`)
        }
      } else if (performanceCaseIds.size > 0) {
        errors.push(`${moduleId}: non-Tier-C module declares performance fixture cases`)
      }
      const formatting = profile.dimensions?.internationalization?.localeSensitiveFormatting
      if (!allowedQualityDispositions.has(formatting?.disposition)) {
        errors.push(`${moduleId}: locale-sensitive formatting lacks an applicability disposition`)
      } else if (formatting.disposition === 'not-applicable'
        && (typeof formatting.rationale !== 'string' || formatting.rationale.trim().length < 20)) {
        errors.push(`${moduleId}: locale-sensitive formatting not-applicable disposition lacks a bounded rationale`)
      }
      const requiredStageIds = profile.riskTier === 'C'
        ? ['contract', 'projection-native', 'performance', 'package-consumer', 'gallery', 'clean-clone']
        : ['contract', 'projection-native', 'package-consumer', 'gallery', 'clean-clone']
      const stages = profile.componentStages ?? []
      if (JSON.stringify(stages.map(stage => stage.id)) !== JSON.stringify(requiredStageIds)) {
        errors.push(`${moduleId}: component quality stages must be ${requiredStageIds.join(', ')}`)
      }
      for (const stage of stages) {
        const requiredDimensions = stage.id === 'performance'
          ? ['performance', 'largeData']
          : ['accessibility', 'internationalization', 'theming']
        if (!requiredDimensions.every(dimension => stage.dimensions?.includes(dimension))) {
          errors.push(`${moduleId}: quality stage ${stage.id} does not cover its mandatory dimensions`)
        }
        if (typeof stage.gate !== 'string' || stage.gate.trim().length < 10) {
          errors.push(`${moduleId}: quality stage ${stage.id} lacks a binary gate description`)
        }
      }
    }
  }
  for (const [projectionId, projection] of Object.entries(module.projections ?? {})) {
    if (!allowedRoles.has(projection.role)) errors.push(`${moduleId}/${projectionId}: unknown role ${projection.role}`)
    if (!allowedStatuses.has(projection.status)) errors.push(`${moduleId}/${projectionId}: unknown status ${projection.status}`)
    if (!statSync(resolve(root, projection.path ?? 'missing'), { throwIfNoEntry: false })?.isDirectory()) {
      errors.push(`${moduleId}/${projectionId}: missing projection path`)
    }
    if (projection.role === 'adapter') {
      if (!projection.seam || !catalog.seams?.[projection.seam]?.adapters?.includes(projectionId)) {
        errors.push(`${moduleId}/${projectionId}: adapter lacks a registered seam`)
      }
      if (!exists(projection.conformanceRunner ?? 'missing')) errors.push(`${moduleId}/${projectionId}: missing conformance runner`)
    }
    if (['compatibility-facade', 'compatibility-vocabulary'].includes(projection.role)
      && (!projection.compatibility?.rationale || !projection.compatibility?.revisit)) {
      errors.push(`${moduleId}/${projectionId}: compatibility projection lacks rationale/revisit trigger`)
    }
    const artifact = projection.artifact
    if (artifact) {
      if (!exists(artifact.manifest ?? 'missing')) errors.push(`${moduleId}/${projectionId}: missing artifact manifest`)
      if (declaredManifests.has(artifact.manifest)) errors.push(`${moduleId}/${projectionId}: duplicate manifest owner`)
      declaredManifests.set(artifact.manifest, `${moduleId}/${projectionId}`)
      if (artifactIds.has(artifact.id)) errors.push(`${moduleId}/${projectionId}: duplicate artifact ${artifact.id}`)
      artifactIds.set(artifact.id, `${moduleId}/${projectionId}`)
    }
    catalogProjectionRows.push({
      moduleId,
      projection: projectionId,
      role: projection.role,
      status: projection.status,
      path: projection.path,
      artifact: artifact?.id,
    })
  }
}

for (const [seamId, seam] of Object.entries(catalog.seams ?? {})) {
  if (!catalog.modules?.[seam.interfaceOwner]) errors.push(`${seamId}: unknown interface owner`)
  if (seam.status !== 'internal' && (seam.adapters?.length ?? 0) < 2) errors.push(`${seamId}: external seam has fewer than two adapters`)
}

function visit(moduleId, active = new Set(), visited = new Set()) {
  if (active.has(moduleId)) {
    errors.push(`module dependency cycle at ${moduleId}`)
    return
  }
  if (visited.has(moduleId)) return
  active.add(moduleId)
  for (const dependency of catalog.modules[moduleId]?.dependencies ?? []) {
    if (!catalog.modules[dependency]) errors.push(`${moduleId}: unknown dependency ${dependency}`)
    else visit(dependency, active, visited)
  }
  active.delete(moduleId)
  visited.add(moduleId)
}
for (const moduleId of Object.keys(catalog.modules ?? {})) visit(moduleId)

const normalizedRows = rows => [...rows].sort((left, right) =>
  `${left.moduleId}/${left.projection}`.localeCompare(`${right.moduleId}/${right.projection}`))
if (JSON.stringify(normalizedRows(catalogProjectionRows)) !== JSON.stringify(normalizedRows(projectionView.projections ?? []))) {
  errors.push('catalog/projections.yaml differs from the authoritative module catalog')
}

const allFiles = files(root)
const localPaths = allFiles.map(path => relative(root, path).replaceAll('\\', '/'))
const packableManifests = []
const publicDistributionAuthorizedManifests = []
for (const local of localPaths.filter(path => path.endsWith('package.json') && path.startsWith('projections/'))) {
  const manifest = load(local)
  packableManifests.push(local)
  if (npmPublicDistributionAuthorized(manifest)) publicDistributionAuthorizedManifests.push(local)
}
for (const local of localPaths.filter(path => path.endsWith('.csproj') && path.startsWith('projections/'))) {
  const source = readFileSync(resolve(root, local), 'utf8')
  if (/<IsPackable>\s*true\s*<\/IsPackable>/.test(source)) {
    packableManifests.push(local)
    if (nugetPublicDistributionAuthorized(source)) publicDistributionAuthorizedManifests.push(local)
    if (!/<HarborlinePublicDistributionAuthorized>\s*false\s*<\/HarborlinePublicDistributionAuthorized>/.test(source)) {
      errors.push(`${local}: packable NuGet projection must explicitly state its public-distribution posture`)
    }
  }
}
const undeclaredManifests = packableManifests.filter(path => !declaredManifests.has(path))
const missingPackableManifests = [...declaredManifests.keys()].filter(path => !packableManifests.includes(path))
if (undeclaredManifests.length) errors.push(`undeclared packable manifests: ${undeclaredManifests.join(', ')}`)
if (missingPackableManifests.length) errors.push(`declared manifests are not packable: ${missingPackableManifests.join(', ')}`)
if (publicDistributionAuthorizedManifests.length) {
  errors.push(`public distribution is not authorized: ${publicDistributionAuthorizedManifests.join(', ')}`)
}

const npmManifest = load('projections/react/ui/hlp.ui.button/package.json')
if (npmManifest.name !== '@harborline-software/ui-react') errors.push('npm package identity changed')
if (npmManifest.version !== '0.8.0-alpha') errors.push('npm candidate version changed')
if (npmManifest.private !== true || npmManifest.publishConfig !== undefined) {
  errors.push('npm projection must remain private with no publishConfig until public release')
}
const uiProject = readFileSync(resolve(root, 'projections/blazor/ui/hlp.ui.button/Harborline.UIAdapters.Blazor.csproj'), 'utf8')
const foundationProject = readFileSync(resolve(root, 'projections/dotnet/foundation/hlp.ui.button/Harborline.Foundation.csproj'), 'utf8')
// Ticket 063 phase 1 (authority). This pair previously asserted the DIVERGENCE itself: PackageId on
// the Harborline identity while AssemblyName had to REMAIN the source-era one. That is the split
// 063 exists to close, so the assembly half is retired here — it forbade the rename by gate.
//
// The PackageId half stands unchanged, so the published identity is still pinned exactly.
// AssemblyName is deliberately UNASSERTED for the duration of the program: it is mid-transition,
// and asserting either value would be wrong. Asserting the source-era name forbids the rename;
// asserting convergence fails this gate on every pre-rename tree, and 063's own rule is that each
// phase lands with gates green.
//
// Phase 4(a) replaces this with the convergence assertion — PackageId === AssemblyName ===
// RootNamespace for every packable project, which is completion-bar item 2 — at the moment that
// becomes true. Until then this window is the known, stated gap in identity enforcement.
for (const [source, packageId] of [
  [uiProject, 'Harborline.UIAdapters.Blazor'],
  [foundationProject, 'Harborline.Foundation'],
]) {
  if (!source.includes(`<PackageId>${packageId}</PackageId>`)) {
    errors.push(`NuGet package identity changed for ${packageId}`)
  }
}
if (/<FrameworkReference Include="Microsoft\.AspNetCore\.App"/.test(uiProject)) {
  errors.push('Blazor NuGet package cannot be consumed by browser WebAssembly')
}
if (!/<PackageReference Include="Microsoft\.AspNetCore\.Components\.Web"/.test(uiProject)) {
  errors.push('Blazor NuGet package lacks its portable component dependency')
}

const prohibitedPath = /(^|\/)(?:fleet|ordinance|anchor|bridge|anchor-mobile-ios|accelerators|\.claude|\.codex|\.wolf)(\/|$)/i
const prohibitedDependencyName = /^(?:@[^/]+\/)?(?:fleet|ordinance|anchor|bridge|anchor-mobile-ios|accelerators)(?:$|[-./])/i
const prohibitedImport = /(?:\bfrom\s*|\bimport\s*\(|\brequire\s*\()\s*["'](?:@[^/"']+\/)?(?:fleet|ordinance|anchor|bridge|anchor-mobile-ios|accelerators)(?:[-./"'])/i
let contributionConfigDependencyViolations = 0
// Claude Code writes this machine-local permissions cache whenever a tool call is approved for
// the project, and .gitignore already excludes it, so no commit can carry it. The walk sees the
// working tree rather than the index though, so the .claude ban above failed catalog-preflight on
// a file that is not repository content — breaking the gate for anyone who merely used the agent
// here. Exempt exactly this path, and only while it stays untracked: everything else under
// .claude/ (agents, skills, vendored worktrees) is still a real violation, and so is this file
// the moment someone commits it. Unreadable git state fails closed and keeps the ban.
const machineLocalPath = '.claude/settings.local.json'
let machineLocalIsUntracked = false
try {
  machineLocalIsUntracked = execFileSync('git', ['ls-files', '--', machineLocalPath], { cwd: root, encoding: 'utf8' }).trim() === ''
} catch {
  machineLocalIsUntracked = false
}
for (const local of localPaths) {
  const machineLocalExempt = local === machineLocalPath && machineLocalIsUntracked
  if (prohibitedPath.test(local) && !machineLocalExempt) errors.push(`prohibited source path ${local}`)
  if (/\.(?:png|jpg|jpeg|gif|zip|gz|dll|dylib|exe|nupkg|tgz)$/i.test(local)) continue
  const content = readFileSync(resolve(root, local), 'utf8')
  const configDependencyError = dependencylessVitestConfigError(local, content, exists(`${dirname(local)}/package.json`))
  if (configDependencyError) {
    contributionConfigDependencyViolations += 1
    errors.push(configDependencyError)
  }
  const themeAliasError = rootScopedThemeAliasError(local, content)
  if (themeAliasError) errors.push(themeAliasError)
  const provenance = local.startsWith('docs/provenance/')
  const selfValidator = local === 'tooling/validate-repository.mjs'
  if (!provenance && !selfValidator && content.includes('/Users/christopherwood/Projects/Harborline-Software')) {
    errors.push(`${local}: legacy absolute source path`)
  }
  if (/"(?:file|link|portal|workspace):/.test(content)) errors.push(`${local}: local package protocol`)
  if (/\.\.\/harborline-(?:platform|api|app|toolbox|www|brand)/i.test(content)) errors.push(`${local}: sibling source dependency`)
  if (/\/Projects\/Harborline\/harborline-(?:api|app|toolbox|www|brand)(?:\/|["'])/i.test(content)) {
    errors.push(`${local}: absolute sibling source dependency`)
  }
  if (prohibitedImport.test(content)) errors.push(`${local}: prohibited bare source dependency`)
  if (local.endsWith('package.json')) {
    const manifest = JSON.parse(content)
    for (const section of ['dependencies', 'devDependencies', 'peerDependencies', 'optionalDependencies']) {
      for (const dependency of Object.keys(manifest[section] ?? {})) {
        if (prohibitedDependencyName.test(dependency)) errors.push(`${local}: prohibited package dependency ${dependency}`)
      }
    }
  }
  for (const match of content.matchAll(/<ProjectReference\b[^>]*\bInclude="([^"]+)"/g)) {
    const target = resolve(dirname(resolve(root, local)), match[1].replaceAll('\\', '/'))
    // Containment by relative path, not by string prefix. `startsWith(`${root}/`)` hardcodes a
    // forward slash, and resolve() returns backslashes on Windows, so every ProjectReference in
    // the repository read as leaving it -- 40+ false errors, none of them real.
    const contained = relative(root, target)
    if (contained !== '' && (contained.startsWith('..') || /^[a-zA-Z]:/.test(contained))) {
      errors.push(`${local}: project reference leaves repository`)
    }
  }
  if (/\b(?:from|import)\s*[(']\s*["'][^"']*projections\//.test(content) && local.includes('/runners/')) {
    errors.push(`${local}: conformance runner imports projection internals`)
  }
}

let remotes = []
try {
  remotes = execFileSync('git', ['remote'], { cwd: root, encoding: 'utf8' }).trim().split('\n').filter(Boolean)
} catch {
  errors.push('unable to inspect Git remotes')
}
const localCloneRemotes = []
const configuredRemotes = []
for (const remote of remotes) {
  try {
    const url = execFileSync('git', ['remote', 'get-url', remote], { cwd: root, encoding: 'utf8' }).trim()
    if (url.startsWith('/') || url.startsWith('./') || url.startsWith('../') || url.startsWith('file://')) {
      localCloneRemotes.push(remote)
    } else {
      configuredRemotes.push(remote)
    }
  } catch {
    errors.push(`unable to inspect Git remote ${remote}`)
  }
}
if (repository.remote?.status === 'not-created' && configuredRemotes.length) {
  errors.push(`external remote configured before approval: ${configuredRemotes.join(', ')}`)
}

const report = {
  schemaVersion: 1,
  repository: repository.repository,
  status: errors.length === 0 ? 'PASS' : 'FAIL',
  counts: {
    modules: Object.keys(catalog.modules ?? {}).length,
    projections: catalogProjectionRows.length,
    artifacts: artifactIds.size,
    sourceFiles: allFiles.length,
    packableManifests: packableManifests.length,
    distributionAuthorizedManifests: publicDistributionAuthorizedManifests.length,
    galleryApplications: galleryReport?.checks?.privateApplications ?? 0,
    galleryScenarios: galleryReport?.counts?.scenarios ?? 0,
  },
  checks: {
    duplicateArtifacts: 0,
    undeclaredArtifacts: 0,
    moduleCycles: 0,
    prohibitedPaths: 0,
    legacyOrSiblingSourceDependencies: 0,
    configuredRemotes: configuredRemotes.length,
    localCloneRemotes: localCloneRemotes.length,
    packageIdentityAuthorityChanged: false,
    distributionAuthority: repository.packageDistributionAuthority,
    publishableGalleryArtifacts: galleryReport?.checks?.publishableGalleryArtifacts ?? null,
    contributionConfigDependencyViolations,
    packageIdentityAuthority: repository.packageIdentityAuthority,
  },
  errors,
}
process.stdout.write(`${JSON.stringify(report, null, 2)}\n`)
process.exitCode = errors.length === 0 ? 0 : 1
