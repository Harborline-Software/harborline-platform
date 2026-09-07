#!/usr/bin/env node

import { spawnSync } from 'node:child_process'
import { existsSync, readdirSync, rmSync, statSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { computePackageVersion, PROPS_RELATIVE_PATH, readPackageVersionProps } from './package-version.mjs'
import { resolveAppshellFeed } from './resolve-appshell-feed.mjs'
import { runnerEnvironment, spawnWithBudget, stepBudgetMs } from './resolve-command.mjs'
import { resolvePinnedDotnet } from './resolve-dotnet.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const reactGallery = resolve(root, 'gallery/projections/react')
const galleryTests = resolve(root, 'gallery/tests')
const blazorProject = resolve(root, 'gallery/projections/blazor/Harborline.Gallery.Blazor.csproj')
const contractsNpmArtifact = resolve(root, 'artifacts/packages/npm/harborline-software-contracts-0.0.0-alpha.0.tgz')
const ruleEngineNpmArtifact = resolve(root, 'artifacts/packages/npm/harborline-software-rule-engine-0.1.0-alpha.0.tgz')
const npmArtifact = resolve(root, 'artifacts/packages/npm/harborline-software-ui-react-0.8.0-alpha.tgz')
const nugetFeed = resolve(root, 'artifacts/packages/nuget')
const packageCache = resolve(root, '.packages/gallery')
const packageArtifactSpecs = [
  {
    artifact: contractsNpmArtifact,
    inputs: [resolve(root, 'projections/typescript/contracts/hlp.contracts.forms')],
  },
  {
    artifact: ruleEngineNpmArtifact,
    inputs: [resolve(root, 'projections/typescript/foundation/hlp.foundation.rule-runtime')],
  },
  {
    artifact: npmArtifact,
    inputs: [resolve(root, 'projections/react/ui/hlp.ui.button')],
  },
]
const nugetInputRoots = [resolve(root, 'Directory.Build.props'), resolve(root, 'Directory.Packages.props'), resolve(root, 'projections/dotnet'), resolve(root, 'projections/blazor')]

function filesUnder(path) {
  if (!existsSync(path)) throw new Error(`package-producing input is missing: ${path}`)
  const entry = statSync(path)
  if (entry.isFile()) return [path]
  return readdirSync(path, { withFileTypes: true })
    .filter(child => !['bin', 'obj', 'node_modules', 'dist'].includes(child.name))
    .flatMap(child => filesUnder(resolve(path, child.name)))
}

export function inspectPackageArtifacts(specs) {
  return specs.map(({ artifact, inputs }) => {
    if (!existsSync(artifact) || !statSync(artifact).isFile()) {
      return { artifact, status: 'missing' }
    }
    const newestInput = inputs.flatMap(filesUnder)
      .map(path => ({ path, mtimeMs: statSync(path).mtimeMs }))
      .sort((left, right) => right.mtimeMs - left.mtimeMs)[0]
    if (!newestInput) throw new Error(`package artifact has no producing inputs: ${artifact}`)
    const artifactMtimeMs = statSync(artifact).mtimeMs
    return {
      artifact,
      status: artifactMtimeMs >= newestInput.mtimeMs ? 'fresh' : 'stale',
      artifactMtime: new Date(artifactMtimeMs).toISOString(),
      newestInput: newestInput.path,
      newestInputMtime: new Date(newestInput.mtimeMs).toISOString(),
    }
  })
}

function packageReadiness() {
  const nugetArtifacts = existsSync(nugetFeed)
    ? readdirSync(nugetFeed, { withFileTypes: true })
      .filter(entry => entry.isFile() && /^(?:Harborline|Shipyard)\..+\.nupkg$/i.test(entry.name) && !entry.name.endsWith('.symbols.nupkg'))
      .map(entry => ({ artifact: resolve(nugetFeed, entry.name), inputs: nugetInputRoots }))
    : []
  if (nugetArtifacts.length === 0) nugetArtifacts.push({ artifact: resolve(nugetFeed, '<first-party-package>.nupkg'), inputs: nugetInputRoots })
  return inspectPackageArtifacts([...packageArtifactSpecs, ...nugetArtifacts])
}

function relativeReadiness(entries) {
  return entries.map(entry => ({
    ...entry,
    artifact: entry.artifact.startsWith(root) ? entry.artifact.slice(root.length + 1).replaceAll('\\', '/') : entry.artifact,
    newestInput: entry.newestInput?.startsWith(root) ? entry.newestInput.slice(root.length + 1).replaceAll('\\', '/') : entry.newestInput,
  }))
}

// verify-package-fixtures.mjs is ONE step, and its only recorded duration is 66 minutes -- in a
// lane run that eventually went green (5.4 min in the quiet reproduction). The 900s step budget
// would kill that slow-but-correct run, so it gets its own: 150 min, above the recorded worst case
// with margin, overridable with HARBORLINE_GALLERY_FIXTURES_BUDGET_MS. The per-invocation budget
// inside it (runFixtureStep) is what actually names a wedge; this one only stops "forever".
export function fixturesBudgetMs() {
  return Number(process.env.HARBORLINE_GALLERY_FIXTURES_BUDGET_MS ?? 9_000_000) || 9_000_000
}

// Ticket 275: the reproduced 66-minute "hang" lived in the steps this function runs -- npm ci, npm
// install, and (through verify-package-fixtures.mjs) ~20 serial dotnet restore --force --no-cache,
// none of them with a timeout or anything on stdout. Same budget/breadcrumb/named-failure shape as
// run-gallery-gate.mjs's run(). This budget bounds a step AS A WHOLE and can only ever say "the
// fixture step"; naming WHICH restore wedged is runFixtureStep's per-invocation budget.
export function run(executable, args, cwd = root, extraEnv = {}, budgetMs = stepBudgetMs()) {
  process.stderr.write(`gallery prepare step started (budget ${budgetMs}ms): ${[executable, ...args].join(' ')}\n`)
  const result = spawnWithBudget(executable, args, { cwd, extraEnv, budgetMs })
  if (result.error?.code === 'ETIMEDOUT') {
    throw new Error(`prepareGalleries step exceeded its ${budgetMs}ms budget and was killed: ${[executable, ...args].join(' ')}`)
  }
  if (result.status !== 0) {
    throw new Error(`${[executable, ...args].join(' ')} failed\n${result.stdout}\n${result.stderr}`)
  }
}

function removeCacheWithRetry(directory, dotnet, attempts = 6) {
  let shutdownTried = false
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    try {
      rmSync(directory, { recursive: true, force: true })
      return
    } catch (error) {
      if (error?.code !== 'EPERM' && error?.code !== 'EBUSY') throw error
      // The holder is almost always the dotnet build server, which keeps handles under the NuGet
      // cache indefinitely -- so waiting longer never wins. Shut it down once, then keep retrying.
      // Confirmed by hand: `dotnet build-server shutdown` released the directory immediately after
      // six backoff attempts had failed.
      if (!shutdownTried && dotnet?.executable) {
        shutdownTried = true
        spawnSync(dotnet.executable, ['build-server', 'shutdown'], {
          encoding: 'utf8', env: { ...process.env, ...runnerEnvironment },
        })
      }
      if (attempt === attempts) {
        throw new Error(
          `${directory} could not be removed after ${attempts} attempts (${error.code}). Something still `
          + 'holds a handle under it. `dotnet build-server shutdown` was already attempted, so this is '
          + 'something else -- an indexer, an editor, or a gallery process from a previous run. Delete '
          + 'the directory once nothing is running.')
      }
      // Busy-wait rather than async sleep: prepareGalleries is synchronous and its callers are too.
      const until = Date.now() + attempt * 500
      while (Date.now() < until) { /* back off */ }
    }
  }
}

// Ticket 143. The version the gallery is about to ask NuGet for is the one stamped into
// packed-version.props, and it has to be the version the sources on disk produce. Otherwise the
// restore either fails at the feed or, worse, succeeds against an earlier build of the adapters and
// the compiler reports a type error in a story nobody edited. NuGet's own error for a missing
// version (NU1102) names a package, not a cause, so say the cause here first.
export function assertFeedMatchesSources() {
  const expected = computePackageVersion(root)
  const stamped = readPackageVersionProps(root)
  const nupkg = resolve(nugetFeed, `Harborline.UIAdapters.Blazor.${expected}.nupkg`)
  if (stamped === expected && existsSync(nupkg)) return expected
  throw new Error([
    'the packed NuGet feed does not match the sources on disk, so the gallery would compile against an earlier build of the adapters.',
    `  sources produce: ${expected}`,
    `  ${PROPS_RELATIVE_PATH} stamps: ${stamped ?? '(nothing packed yet)'}`,
    `  feed has that .nupkg: ${existsSync(nupkg)}`,
    'Repack with `npm run gallery:prepare` (or `node tooling/verify-package-fixtures.mjs`). Nothing needs',
    'deleting from ~/.nuget/packages: every pack carries its own content-derived version, so no',
    'extraction can shadow another and no build-server handle can get in the way.',
  ].join('\n'))
}

export function prepareGalleries({ packagesReady = false } = {}) {
  const dotnet = resolvePinnedDotnet(root)
  // The app-shell fixture stages two cross-repo tarballs from environment paths, and
  // verify-package-fixtures dies without them. run-phase-4-gate.mjs resolves them for its
  // package-consumers step and the gallery inherits them from there -- so the gate worked while
  // every STANDALONE entry point (capture:gallery, test:gallery) failed with
  // "environment tarball path missing", which reads like a broken checkout rather than a missing
  // resolution step. Resolving here fixes all of them at once.
  //
  // resolveAppshellFeed is idempotent and cache-aware: a correct operator-supplied path wins, a
  // wrong one self-heals from cache, and a miss rebuilds from the pinned refs. So calling it when
  // the gate has already set the environment costs a sha256 check, not a rebuild.
  if (!packagesReady) {
    process.stderr.write('preparing galleries: verifying package fixtures\n')
    run(process.execPath, ['tooling/verify-package-fixtures.mjs'], root, resolveAppshellFeed(), fixturesBudgetMs())
  }
  const readiness = packageReadiness()
  const unusable = readiness.filter(entry => entry.status !== 'fresh')
  if (unusable.length) {
    throw new Error(`packed package artifacts are not ready:\n${JSON.stringify(relativeReadiness(unusable), null, 2)}`)
  }
  assertFeedMatchesSources()

  run('npm', ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], reactGallery)
  run('npm', [
    'install', '--no-save', '--package-lock=false', '--ignore-scripts', '--no-audit', '--no-fund',
    contractsNpmArtifact, ruleEngineNpmArtifact, npmArtifact,
  ], reactGallery)
  run('npm', ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], galleryTests)
  // Windows returns EPERM here when anything still holds a handle under the NuGet package cache --
  // a dotnet build server or a virus scanner that has not let go yet. `force: true` does NOT cover
  // it: force suppresses "missing", not "locked". Four gate runs failed on this, each reporting
  // "Permission denied" as though the checkout were broken rather than "a previous run has not
  // finished letting go".
  //
  // Retry with a short backoff, then fail with a message that names the real cause. Retrying a
  // delete is safe: the directory is a rebuildable cache, never a source of truth.
  removeCacheWithRetry(packageCache, dotnet)
  // Sources come from the nuget.config beside the gallery project, not double-dash-source
  // arguments: MSBuild property plumbing under the BlazorWebAssembly SDK backslash-normalizes
  // URL sources into nonexistent local paths on Windows (NU1301).
  run(dotnet.executable, [
    'restore', blazorProject,
    '--packages', packageCache,
    '--force', '--no-cache', '-v:minimal',
  ])

  return { dotnet, reactGallery, galleryTests, blazorProject, contractsNpmArtifact, npmArtifact, nugetFeed, packageCache, reusedArtifacts: packagesReady ? relativeReadiness(readiness) : [] }
}

if (import.meta.main) {
  // `--assert-feed` is the whole gallery freshness check with none of the preparation: the Blazor
  // project runs it before every build so a developer at a bare `dotnet build` prompt is told the
  // feed is behind their edits, by name, instead of compiling against the last pack and reading the
  // result as a broken story file.
  if (process.argv.includes('--assert-feed')) {
    try {
      process.stdout.write(`${assertFeedMatchesSources()}\n`)
    } catch (error) {
      process.stderr.write(`${error instanceof Error ? error.message : error}\n`)
      process.exitCode = 1
    }
  } else {
  try {
    const prepared = prepareGalleries({ packagesReady: process.argv.includes('--packages-ready') })
    process.stdout.write(`${JSON.stringify({
      schemaVersion: 1,
      status: 'PASS',
      dotnetSdk: prepared.dotnet.version,
      contractsNpmArtifact: 'artifacts/packages/npm/harborline-software-contracts-0.0.0-alpha.0.tgz',
      npmArtifact: 'artifacts/packages/npm/harborline-software-ui-react-0.8.0-alpha.tgz',
      nugetFeed: 'artifacts/packages/nuget',
      reusedArtifacts: prepared.reusedArtifacts,
      sourceDependencies: 0,
    }, null, 2)}\n`)
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : error}\n`)
    process.exitCode = 1
  }
}
}
