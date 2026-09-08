#!/usr/bin/env node

import { spawn, spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs'
import { get } from 'node:http'
import { dirname, resolve } from 'node:path'
import { createServer } from 'node:net'
import { tmpdir } from 'node:os'
import { fileURLToPath } from 'node:url'

import { expectedChecks, observeGalleryRun, reconcileChecks } from './gallery-observations.mjs'
import { registeredFlakeRecords, unregisteredFlakes, validateFlakeRegistry } from './flake-registry.mjs'
import { recordGalleryBaselines } from './gallery-baseline-store.mjs'
import { prepareGalleries } from './prepare-galleries.mjs'
import { mergeShardReports } from './gallery-shard-merge.mjs'
import { resolveCommand, runnerEnvironment, spawnWithBudget, stepBudgetMs } from './resolve-command.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const galleryModules = readdirSync(resolve(root, 'gallery/scenarios'))
  .filter(name => /^hlp\.ui\..+\.json$/.test(name))
  .sort()
  .map(name => {
  const catalogPath = `gallery/scenarios/${name}`
  const catalog = JSON.parse(readFileSync(resolve(root, catalogPath), 'utf8'))
  const quality = JSON.parse(readFileSync(resolve(root, catalog.qualityFixture), 'utf8'))
  return {
    moduleId: catalog.moduleId,
    scenarios: catalog.scenarios,
    scenarioCount: catalog.scenarios.length,
    visualParityScenarios: catalog.scenarios.filter(scenario =>
      scenario.sourceQualityCaseIds?.some(id => id.endsWith('.quality.visual-parity'))).length,
    reflowScenarios: catalog.scenarios.filter(scenario =>
      scenario.sourceQualityCaseIds?.some(id => id.endsWith('.quality.reflow'))).length,
    locales: Object.keys(quality.locales ?? {}).length,
    themes: (quality.themes ?? []).length,
    qualityCases: quality.accessibilityCases.length + quality.internationalizationCases.length + quality.themingCases.length + (quality.dismissalCases ?? []).length,
  }
})
const scenarioCount = galleryModules.reduce((total, module) => total + module.scenarioCount, 0)
const localeCount = galleryModules.reduce((total, module) => total + module.locales, 0)
const qualityCaseCount = galleryModules.reduce((total, module) => total + module.qualityCases, 0)
const results = []
const servers = []
// Ticket 254: the ports are overridable so a measurement run can coexist with a gate holding
// 6106/6107. The DEFAULTS are unchanged, so the ordinary gate is byte-for-byte what it was.
const reactPort = Number(process.env.HARBORLINE_GALLERY_REACT_PORT ?? 6106) || 6106
const blazorPort = Number(process.env.HARBORLINE_GALLERY_BLAZOR_PORT ?? 6107) || 6107
const reactGalleryUrl = `http://127.0.0.1:${reactPort}`
const blazorGalleryUrl = `http://127.0.0.1:${blazorPort}`
const detached = process.platform !== 'win32'
const focusedPattern = process.env.GALLERY_GREP
// Ticket 098: "Selection reduces work; sharding reduces wall-clock. Both are required." Default 1
// keeps the ordinary gate unchanged; set GALLERY_SHARDS to split the suite across that many
// concurrent Playwright invocations.
const shardCount = Math.max(1, Number(process.env.GALLERY_SHARDS ?? 1) || 1)
const recordBaselines = process.argv.includes('--record')
const captureGallery = process.argv.includes('--capture') || /^(1|true)$/i.test(process.env.HARBORLINE_CAPTURE_GALLERY ?? '')
if (recordBaselines && focusedPattern) throw new Error('--record requires the complete gallery gate; GALLERY_GREP is not allowed')
const baselineCaptureRoot = recordBaselines ? mkdtempSync(resolve(tmpdir(), 'hlp-gallery-baselines-')) : undefined
const captureRunId = new Date().toISOString().replaceAll(':', '-').replace(/\.\d{3}Z$/, 'Z')
const galleryCaptureRoot = captureGallery
  ? resolve(process.env.HARBORLINE_GALLERY_CAPTURE_DIR ?? resolve(root, 'artifacts/gallery-screenshots', captureRunId))
  : undefined
if (galleryCaptureRoot) mkdirSync(galleryCaptureRoot, { recursive: true })

// Ticket 098 phase 3, the SHARDING half. Playwright --shard splits the suite across independent
// invocations; run concurrently they use cores that `workers` alone leaves idle, because a single
// invocation is capped by its own worker pool.
//
// Off by default (GALLERY_SHARDS unset or 1) so the ordinary gate is byte-for-byte what it was. When
// set, each shard writes its OWN json report and they are merged before observation -- which is not
// bookkeeping: reconciliation demands every declared scenario be observed, so a shard that silently
// failed to run shows up as declared-but-not-run rather than as a smaller, quietly passing suite.
function runShardedGallery(id, cwd, extraEnv, shardCount) {
  const started = performance.now()
  // Shards DIVIDE a worker budget, they do not multiply one. Four shards at the config's four
  // workers put sixteen browsers plus two gallery servers on sixteen cores: the suite got faster
  // (9.0 -> 5.2 min) and grew fourteen flaky tests and one failure that did not reproduce alone.
  // A gate that wins wall-clock by becoming unreliable has traded the thing it exists to provide.
  const totalWorkers = Math.max(1, Number(process.env.PW_WORKERS ?? 8) || 8)
  const workersPerShard = Math.max(1, Math.floor(totalWorkers / shardCount))
  const shards = Array.from({length: shardCount}, (_, index) => {
    const shard = index + 1
    const reportPath = `test-results/results-shard-${shard}.json`
    const resolved = resolveCommand('npm', ['test', '--', `--shard=${shard}/${shardCount}`])
    return new Promise(resolveShard => {
      const child = spawn(resolved.executable, resolved.args, {
        cwd,
        env: {
          ...process.env, ...runnerEnvironment, ...extraEnv,
          PLAYWRIGHT_JSON_OUTPUT_NAME: reportPath,
          PW_WORKERS: String(workersPerShard),
        },
      })
      let output = ''
      child.stdout.on('data', chunk => { output += chunk })
      child.stderr.on('data', chunk => { output += chunk })
      child.on('close', code => resolveShard({shard, code, output, reportPath}))
    })
  })

  return Promise.all(shards).then(finished => {
    const failed = finished.filter(shardResult => shardResult.code !== 0)
    // Merge every shard's report into the single file the observation path already reads, so
    // sharding changes how the suite RUNS and nothing about how it is judged.
    const merged = mergeShardReports(finished.map(shardResult => {
      const full = resolve(cwd, shardResult.reportPath)
      return existsSync(full) ? JSON.parse(readFileSync(full, 'utf8')) : null
    }))
    writeFileSync(resolve(cwd, 'test-results/results.json'), `${JSON.stringify(merged)}\n`)

    const entry = {
      id,
      exitCode: failed.length === 0 ? 0 : (failed[0].code ?? 1),
      durationMs: Math.round(performance.now() - started),
      passed: failed.length === 0,
      shards: finished.map(shardResult => ({shard: shardResult.shard, exitCode: shardResult.code})),
      failureOutput: failed.length === 0 ? undefined
        : failed.map(shardResult => `--- shard ${shardResult.shard}/${shardCount} ---\n${shardResult.output}`)
            .join('\n').trimEnd().split('\n').slice(-120).join('\n'),
    }
    results.push(entry)
    if (!entry.passed) throw new Error(`${id} failed:\n${entry.failureOutput}`)
  })
}

// Ticket 275: unbounded steps (npm ci, dotnet restore --no-cache, playwright install) made a
// wedge and a slow-but-fine step look identical -- no output, no CPU. The breadcrumb makes the
// silence attributable to a step id; the budget turns "forever" into a named, diagnosable failure.
export function run(id, executable, args, cwd = root, extraEnv = {}) {
  const started = performance.now()
  process.stderr.write(`gallery step ${id} started (budget ${stepBudgetMs()}ms)\n`)
  const result = spawnWithBudget(executable, args, { cwd, extraEnv })
  if (result.error?.code === 'ETIMEDOUT') {
    throw new Error(`${id} exceeded its ${stepBudgetMs()}ms budget and was killed: ${[executable, ...args].join(' ')}`)
  }
  const entry = {
    id,
    exitCode: result.status,
    durationMs: Math.round(performance.now() - started),
    passed: result.status === 0,
    failureOutput: result.status === 0 ? undefined : `${result.stdout}\n${result.stderr}`.trimEnd().split('\n').slice(-120).join('\n'),
  }
  results.push(entry)
  if (!entry.passed) throw new Error(`${id} failed:\n${entry.failureOutput}`)
}

function start(id, executable, args, cwd) {
  const output = []
  const resolved = resolveCommand(executable, args)
  const child = spawn(resolved.executable, resolved.args, {
    cwd,
    detached,
    env: { ...process.env, ...runnerEnvironment },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  child.stdout.on('data', chunk => output.push(chunk.toString()))
  child.stderr.on('data', chunk => output.push(chunk.toString()))
  servers.push({ id, child, output })
}

// Ticket 254: a FLAT 120s budget is not a measurement of anything. Measured on this machine, the
// Storybook dev server answers in 4.0-7.5s and the Blazing Story host in 1.4-2.7s (quiet and with
// a concurrent React suite). QUIET_STARTUP_MS is the slowest observed start rounded up to roughly
// twice it, so a cold Vite prebundle still fits, and the budget is that times
// READY_BUDGET_MULTIPLE. The elapsed time is printed on every wait, success or failure, so the
// next person tuning this has the measurement rather than a number somebody once typed.
const READY_BUDGET_MULTIPLE = 8
const QUIET_STARTUP_MS = {react: 15_000, blazor: 5_000}

async function waitFor(label, url, timeoutMs = QUIET_STARTUP_MS[label] * READY_BUDGET_MULTIPLE) {
  const started = performance.now()
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    try {
      if (await probeUrl(url)) {
        process.stderr.write(`${label} gallery ready at ${url} after ${Math.round(performance.now() - started)}ms (budget ${timeoutMs}ms)\n`)
        return
      }
    } catch {
      // Server is still starting.
    }
    await new Promise(resolveWait => setTimeout(resolveWait, 300))
  }
  throw new Error(`timed out waiting for ${url} after ${Math.round(performance.now() - started)}ms (budget ${timeoutMs}ms)`)
}

function probeUrl(url) {
  return new Promise((resolveProbe, rejectProbe) => {
    const request = get(url, response => {
      response.resume()
      response.once('end', () => resolveProbe(response.statusCode >= 200 && response.statusCode < 400))
    })
    request.once('error', rejectProbe)
    request.setTimeout(5_000, () => request.destroy(new Error(`timed out probing ${url}`)))
  })
}

async function assertPortAvailable(port) {
  await new Promise((resolvePort, rejectPort) => {
    const probe = createServer()
    probe.unref()
    probe.once('error', error => rejectPort(new Error(`gallery port ${port} is unavailable: ${error.message}`)))
    probe.listen(port, '127.0.0.1', () => probe.close(resolvePort))
  })
}

function stopServers() {
  for (const { child } of servers) {
    if (!child.pid || child.killed) continue
    try {
      if (detached) process.kill(-child.pid, 'SIGTERM')
      // On Windows child.kill() reaches only the direct child; the dev server it spawned
      // would survive and hold its port for the next run. taskkill /T fells the whole tree.
      else if (process.platform === 'win32') spawnSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' })
      else child.kill('SIGTERM')
    } catch {
      // The process already exited.
    }
  }
}

// A failed Playwright process can still leave a complete JSON report. Those observations are
// evidence too: suppressing them turned a long browser run into browserTests=0 and hid whether the
// browser launched, how many specs ran, and which outcome failed.
export function observeCompletedGalleryRun({ results, reportPath, scenarioIds }) {
  if (!results.some(result => result.id === 'gallery-accessibility-and-parity')) return undefined
  const raw = JSON.parse(readFileSync(reportPath, 'utf8'))
  return observeGalleryRun(raw, scenarioIds)
}


// Ticket 275: the gate body runs only as the entry point. Everything above is definitions, so a
// self-test can import run() and drive it past a tiny budget; before the guard, importing this
// module probed ports, prepared galleries and started servers, and the export bought nothing.
if (import.meta.main) {
  let prepared
  let failure
  try {
    await Promise.all([assertPortAvailable(reactPort), assertPortAvailable(blazorPort)])
    prepared = prepareGalleries({ packagesReady: process.argv.includes('--packages-ready') })
    run('gallery-structure', process.execPath, ['tooling/validate-gallery.mjs'])
    run('react-gallery-typecheck', 'npm', ['run', 'typecheck'], prepared.reactGallery)
    run('react-storybook-build', 'npm', ['run', 'build'], prepared.reactGallery)
    run('blazor-gallery-clean', prepared.dotnet.executable, [
      'clean', prepared.blazorProject, '--configuration', 'Release', '-v:quiet',
    ])
    run('blazor-gallery-build', prepared.dotnet.executable, [
      'build', prepared.blazorProject, '--configuration', 'Release', '--no-restore', '-v:minimal',
    ])
    run('gallery-tests-typecheck', 'npm', ['run', 'typecheck'], prepared.galleryTests)
    run('playwright-browser', 'npm', ['exec', '--', 'playwright', 'install', 'chromium'], prepared.galleryTests)
    // Installation success only says the package manager completed. Prove the executable can launch
    // under this host's headless/sandbox conditions before a 529-spec run turns that cause into 529
    // indistinguishable failures.
    run('playwright-browser-launch', process.execPath, ['verify-browser.mjs'], prepared.galleryTests)
  
    start('react-storybook', 'npm', [
      'run', 'dev', '--', '--host', '127.0.0.1',
      ...(reactPort === 6106 ? [] : ['--port', String(reactPort)]),
    ], prepared.reactGallery)
    start('blazor-blazing-story', prepared.dotnet.executable, [
      'run', '--project', prepared.blazorProject,
      '--configuration', 'Release', '--no-build', '--no-restore',
      '--urls', blazorGalleryUrl,
    ], resolve(prepared.blazorProject, '..'))
    await Promise.all([waitFor('react', reactGalleryUrl), waitFor('blazor', blazorGalleryUrl)])
    const galleryEnv = {
      ...(reactPort === 6106 ? {} : {REACT_GALLERY_URL: reactGalleryUrl}),
      ...(blazorPort === 6107 ? {} : {BLAZOR_GALLERY_URL: blazorGalleryUrl}),
      ...(baselineCaptureRoot ? { GALLERY_BASELINE_CAPTURE_DIR: baselineCaptureRoot } : {}),
      ...(galleryCaptureRoot ? { HARBORLINE_GALLERY_CAPTURE_DIR: galleryCaptureRoot } : {}),
    }
    // Sharding is refused alongside --grep or --record: both already run a deliberate subset, and
    // splitting a subset across shards would leave most of them with nothing to do.
    if (shardCount > 1 && !focusedPattern && !recordBaselines) {
      await runShardedGallery('gallery-accessibility-and-parity', prepared.galleryTests, galleryEnv, shardCount)
    } else {
      run(
        'gallery-accessibility-and-parity',
        'npm',
        focusedPattern ? ['test', '--', '--grep', focusedPattern] : ['test'],
        prepared.galleryTests,
        galleryEnv,
      )
    }
  } catch (error) {
    failure = error instanceof Error ? error.message : String(error)
  } finally {
    stopServers()
  }
  
  if (failure) {
    for (const server of servers) {
      results.push({
        id: `${server.id}-server-log`,
        passed: false,
        failureOutput: server.output.join('').split('\n').slice(-80).join('\n'),
      })
    }
  }
  
  // Was `results.length === 8`. A bare count cannot say WHICH step is missing or out of order, and
  // eight is exactly the kind of hand-maintained literal control ticket 100 exists to remove.
  const requiredGalleryStepIds = [
    'gallery-structure', 'react-gallery-typecheck', 'react-storybook-build', 'blazor-gallery-clean',
    'blazor-gallery-build', 'gallery-tests-typecheck', 'playwright-browser', 'playwright-browser-launch',
    'gallery-accessibility-and-parity',
  ]
  const gatePassed = !failure
    && results.length === requiredGalleryStepIds.length
    && requiredGalleryStepIds.every((id, index) => results[index]?.id === id && results[index].passed)
  
  // Every count below is derived from the catalog AND from the conditions this run executes under,
  // and every one is COMPARED to a measurement rather than published as one. A family whose
  // measurement has no defensible derivation (contrast-ratio call sites, whose literal the ticket 100
  // classification could not reconstruct from any construct) is deliberately absent and is reported as
  // measured-only. The exemption ids come from the same file gallery.spec.ts skips from.
  const ciGallery = process.env.HARBORLINE_CI_GALLERY === '1'
  const expected = expectedChecks(galleryModules, {
    ciGallery,
    capture: Boolean(galleryCaptureRoot),
    exemptions: JSON.parse(readFileSync(resolve(root, 'gallery/ci-exemptions.json'), 'utf8')),
  })
  
  // What actually ran, read from the json reporter. A focused run is observed too -- the counts it
  // reports are still measurements -- but it does not RECONCILE: it deliberately executes a subset, so
  // a "declared but not run" list and an expectation comparison would both be noise rather than drift.
  // Ticket 284 slice 2: the registry is validated BEFORE the run's outcomes are read, so a stale,
  // unowned or expired registration can never rescue anything — the same order the api half uses. An
  // invalid registry is the gate's failure, and the problems are the failure text, not a bare id: a
  // reader has to be told WHICH row expired (284 slice 1 review, MINOR 1).
  let flakeRegistryRows = []
  let flakeRegistryProblems = []
  try {
    const registry = JSON.parse(readFileSync(resolve(root, 'tooling/flake-registry.json'), 'utf8'))
    flakeRegistryRows = registry.rows
    flakeRegistryProblems = validateFlakeRegistry(flakeRegistryRows, new Date().toISOString().slice(0, 10))
  } catch (error) {
    flakeRegistryProblems = [`tooling/flake-registry.json could not be read: ${error instanceof Error ? error.message : String(error)}`]
  }
  if (flakeRegistryProblems.length > 0 && !failure) {
    failure = `flake registry invalid (tooling/flake-registry.json):\n${flakeRegistryProblems.join('\n')}`
  }

  let observation
  let checkReconciliation
  let flakeRegistryReport
  if (flakeRegistryProblems.length === 0) {
    try {
      observation = observeCompletedGalleryRun({
        results,
        reportPath: resolve(prepared?.galleryTests ?? '', 'test-results/results.json'),
        scenarioIds: galleryModules.flatMap(module => module.scenarios.map(scenario => scenario.id)),
      })
      if (!observation) throw new Error('Playwright did not reach the browser test step')
      // A spec that passed only on retry and is not registered is red the way a failure is, focused
      // run or not: a retry nobody owns is how an intermittent regression becomes a green gate.
      const unregistered = unregisteredFlakes(observation.flakyTests, flakeRegistryRows)
      flakeRegistryReport = {
        registered: registeredFlakeRecords(observation.flakyTests, flakeRegistryRows, observation.flakyAttempts),
        unregistered,
      }
      if (unregistered.length > 0) {
        failure ??= 'unregistered flaky specs (each passed only on its retry; register it in '
          + `tooling/flake-registry.json with an owner and an expiry, or fix it):\n${unregistered.join('\n')}`
      } else if (!focusedPattern) {
        checkReconciliation = reconcileChecks(observation.checks, expected)
        if (observation.reconciliation !== 'exact') failure ??= observation.reconciliation
        else if (checkReconciliation !== 'exact') failure ??= checkReconciliation
      }
    } catch (error) {
      if (!failure) failure = `gallery run observation failed: ${error instanceof Error ? error.message : String(error)}`
    }
  }
  let galleryCapture
  if (gatePassed && galleryCaptureRoot) {
    const entries = readdirSync(resolve(galleryCaptureRoot, 'metadata'))
      .filter(name => name.endsWith('.json'))
      .sort()
      .map(name => JSON.parse(readFileSync(resolve(galleryCaptureRoot, 'metadata', name), 'utf8')))
    const manifest = {
      schemaVersion: 1,
      capturedAt: new Date().toISOString(),
      runId: captureRunId,
      source: 'packed React and Blazor gallery projections',
      counts: {
        scenarios: entries.length,
        reactScreenshots: entries.length,
        blazorScreenshots: entries.length,
        diffScreenshots: entries.filter(entry => entry.diff).length,
      },
      entries,
    }
    writeFileSync(resolve(galleryCaptureRoot, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`)
    galleryCapture = { root: galleryCaptureRoot, ...manifest.counts }
  }
  let baselineRecording
  if (gatePassed && recordBaselines) {
    try {
      baselineRecording = recordGalleryBaselines({ repositoryRoot: root, captureRoot: baselineCaptureRoot, catalogs: galleryModules })
    } catch (error) {
      failure = error instanceof Error ? error.message : String(error)
    }
  }
  if (baselineCaptureRoot) rmSync(baselineCaptureRoot, { recursive: true, force: true })
  const passed = gatePassed && !failure
  process.stdout.write(`${JSON.stringify({
    schemaVersion: 3,
    mode: recordBaselines ? 'record' : focusedPattern ? 'focused-check' : 'complete-gate',
    focusedPattern,
    moduleIds: galleryModules.map(module => module.moduleId),
    status: passed ? (focusedPattern ? 'CHECKED' : 'PASS') : 'FAIL',
    galleryApplications: {
      react: { adapter: 'Storybook', url: reactGalleryUrl, private: true },
      blazor: { adapter: 'Blazing Story', url: blazorGalleryUrl, private: true },
    },
    // `counts` holds only corpus sizes and OBSERVATIONS. Nothing derived lives here any more, so a
    // reader cannot mistake an expectation for a measurement (control ticket 100).
    counts: {
      scenarios: scenarioCount,
      qualityCases: qualityCaseCount,
      localeFixtures: localeCount,
      browserTests: observation?.browserTests ?? 0,
      scenarioBrowserTests: observation?.scenarioBrowserTests ?? 0,
      nonScenarioBrowserTests: observation?.nonScenarioBrowserTests ?? 0,
      // Recorded by the assertions themselves and read back out of the json reporter. A family with no
      // declared expectation is still reported here, as a measurement standing on its own.
      checks: observation?.checks ?? {},
    },
    // `retries: 1` means a test can pass on a second attempt. Playwright calls that flaky, and folding
    // it into the total would hide precisely the signal the retry was allowed in order to surface.
    browserOutcomes: observation?.outcomes,
    // Ticket 284 slice 1: the flaky tests BY NAME, not only as a count. Naming them is what makes a
    // flake ownable; the registry that owns, dates and expires these names is slice 2.
    flakyTests: observation?.flakyTests ?? [],
    // Ticket 284 slice 2: who owns each flake and until when, with BOTH outcomes of its retry; plus
    // the problems that make an invalid registry the gate's failure.
    flakeRegistry: {
      registeredRows: Array.isArray(flakeRegistryRows) ? flakeRegistryRows.length : 0,
      problems: flakeRegistryProblems,
      ...(flakeRegistryReport ?? {registered: [], unregistered: []}),
    },
    // Per scenario: elements compared and elements hidden behind a registered absence, so the
    // register's exemption is visible in the gate's output rather than only in its row count.
    elementParityCoverage: observation?.elementParityCoverage ?? [],
    scenarioReconciliation: observation?.reconciliation,
    checkReconciliation,
    // Declared expectations, kept for COMPARISON only (see `checkReconciliation`). They are never the
    // numbers the gate reports: a count in `counts` was measured by the run that produced it.
    expected,
    modules: galleryModules.map(({ scenarios, ...module }) => module),
    baselineRecording,
    galleryCapture,
    invariants: {
      publicPackageInterfacesOnly: true,
      packedReactArtifacts: [
        'artifacts/packages/npm/harborline-software-contracts-0.0.0-alpha.0.tgz',
        'artifacts/packages/npm/harborline-software-ui-react-0.8.0-alpha.tgz',
      ],
      reusedArtifacts: prepared?.reusedArtifacts ?? [],
      requiredContractsPeerInstalled: true,
      publishableGalleryArtifacts: 0,
      packageIdentityAuthorityChanged: false,
      distributionAuthorityRepository: null,
    },
    results,
    failure,
  }, null, 2)}\n`)
  process.exitCode = passed ? 0 : 1
}
