#!/usr/bin/env node

// Durable feed channel for the two cross-repo tarballs the app-shell fixture stages — the
// "durable feed-channel wiring" ticket 089 left open.
//
// The fixture keeps receiving EXPLICIT paths and keeps verifying sha256 itself; its own comment
// ("staged by explicit environment paths ... never built or searched for implicitly") is a
// deliberate invariant. This module runs BEFORE the fixture and produces those paths. It never
// reaches into the fixture, and the fixture never calls back into it.
//
// The cache is content-addressed: the file NAME is the expected hash, so a hit needs neither
// network nor a harborline-api checkout. Only a miss builds. A build that fails to reproduce the
// pinned hash is fatal — a mismatched artifact is never admitted to the cache.
//
// Reproducibility rests on npm pack being deterministic for these packages: portable tar (entry
// mtimes normalized to 1985-10-26), a zeroed gzip MTIME field, and per-package lockfiles pinning
// tsc to exactly 6.0.2. Both pinned hashes were regenerated from source to confirm it.

import { execFileSync, spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { copyFileSync, cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs'
import { homedir, tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { cleanUpScratchOnSignal, resolveCommand, runnerEnvironment, sweepStaleScratchTrees, writeScratchPidFile } from './resolve-command.mjs'

// Ticket 289: shared with buildArtifact()'s mkdtempSync prefix below and with the self-tests.
const FEED_SCRATCH_PREFIX = 'harborline-feed-'

const moduleDirectory = dirname(fileURLToPath(import.meta.url))
const defaultManifestPath = resolve(moduleDirectory, 'appshell-feed-provenance.json')

function sha256File(path) {
  return createHash('sha256').update(readFileSync(path)).digest('hex')
}

function hasVerifiedCacheEntry(artifact, address, expected) {
  if (!existsSync(address)) return false
  const actual = sha256File(address)
  if (actual !== expected) {
    throw new Error(`${artifact.id}: cache entry ${address} does not match its content-addressed filename.`
      + `\n  expected ${expected}\n  actual   ${actual}`)
  }
  return true
}

function cacheRoot(manifest) {
  const configured = process.env[manifest.cache.pathEnv]
  // The default comes from the manifest's cache.defaultPath rather than a literal here. It was
  // documented in one place and hardcoded in another (ticket 099 item 7).
  if (configured) return resolve(configured)
  const documented = manifest.cache?.defaultPath ?? '~/.harborline/feed-cache'
  return resolve(documented.startsWith('~') ? homedir() + documented.slice(1) : documented)
}

function git(args, cwd) {
  return execFileSync('git', args, { cwd, encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 })
}

function hasCommit(repository, ref) {
  return spawnSync('git', ['cat-file', '-e', `${ref}^{commit}`], { cwd: repository, encoding: 'utf8' }).status === 0
}

function runIn(executable, args, cwd, what) {
  const resolved = resolveCommand(executable, args)
  const result = spawnSync(resolved.executable, resolved.args, {
    cwd,
    encoding: 'utf8',
    maxBuffer: 128 * 1024 * 1024,
    env: { ...process.env, ...runnerEnvironment },
  })
  if (result.status !== 0) {
    const output = (result.stderr || result.stdout || '').trim().slice(-4000)
    throw new Error(`${what} failed: ${executable} ${args.join(' ')} (in ${cwd})\n${output}`)
  }
  return result.stdout ?? ''
}

// A local checkout is preferred (fast, works offline); the bare mirror is the fallback so a
// machine with no harborline-api clone can still rebuild.
function sourceRepository(manifest, neededRefs, cacheDirectory) {
  const pathEnv = manifest.sourceRepository.localPathEnv
  const configured = process.env[pathEnv]
  let repository
  if (configured) {
    repository = resolve(configured)
    if (!existsSync(join(repository, '.git')) && !existsSync(join(repository, 'HEAD'))) {
      throw new Error(`${pathEnv}=${configured} is not a git repository`)
    }
  } else {
    repository = join(cacheDirectory, 'harborline-api.git')
    if (!existsSync(repository)) {
      mkdirSync(cacheDirectory, { recursive: true })
      git(['clone', '--bare', manifest.sourceRepository.remote, repository], cacheDirectory)
    }
  }
  if (neededRefs.some(ref => !hasCommit(repository, ref))) {
    try {
      git(configured ? ['fetch', '--all', '--tags'] : ['fetch', 'origin', '+refs/heads/*:refs/heads/*'], repository)
    } catch {
      // Reported precisely below if the ref is still absent.
    }
  }
  const missing = neededRefs.filter(ref => !hasCommit(repository, ref))
  if (missing.length) {
    throw new Error(`${manifest.sourceRepository.name} at ${repository} lacks pinned ref(s) ${missing.join(', ')}.`
      + ` Set ${pathEnv} to a complete checkout, or allow a clone of ${manifest.sourceRepository.remote}.`)
  }
  return repository
}

// git archive rather than `git worktree add`: it produces a clean tree without mutating the
// source repository's metadata at all. harborline-api declares no export-ignore attributes, so
// the extraction is faithful.
function extractRef(repository, ref, destination) {
  mkdirSync(destination, { recursive: true })
  const archiveName = 'source.tar'
  execFileSync('git', ['archive', '--format=tar', '-o', join(destination, archiveName), ref], { cwd: repository, maxBuffer: 512 * 1024 * 1024 })
  // tar is invoked from `destination` with a RELATIVE archive name, never an absolute path and
  // never -C. GNU tar (the one Git for Windows puts on PATH) reads a leading `C:\...` as a remote
  // host:path spec and fails with "Cannot connect to C: resolve failed".
  const untar = spawnSync('tar', ['-xf', archiveName], { cwd: destination, encoding: 'utf8', maxBuffer: 512 * 1024 * 1024 })
  if (untar.status !== 0) {
    throw new Error(`unable to extract ${ref}: tar exited ${untar.status}.`
      + ` A 'tar' on PATH is required (Windows 10+ ships one).\n${(untar.stderr ?? '').slice(-2000)}`)
  }
  rmSync(join(destination, archiveName), { force: true })
}

function buildArtifact(artifact, manifest, repository, cacheDirectory) {
  const work = mkdtempSync(join(tmpdir(), `${FEED_SCRATCH_PREFIX}${artifact.id}-`))
  writeScratchPidFile(work)
  const disposeSignalCleanup = cleanUpScratchOnSignal(() => { rmSync(work, { recursive: true, force: true }) })
  try {
    extractRef(repository, artifact.ref, work)
    const siblings = (artifact.requiresSiblingBuild ?? []).map(id => {
      const found = manifest.artifacts.find(candidate => candidate.id === id)
      if (!found) throw new Error(`artifact ${artifact.id} requires unknown sibling ${id}`)
      return found
    })
    // Siblings are built at THIS artifact's ref, not at their own: the pins sit at different
    // commits, and each tarball reproduces only against the tree it was cut from.
    for (const sibling of siblings) {
      const siblingDirectory = join(work, sibling.packagePath)
      runIn('npm', ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], siblingDirectory, `${artifact.id}: ${sibling.id} install`)
      runIn('npm', ['run', 'build'], siblingDirectory, `${artifact.id}: ${sibling.id} build`)
    }
    const packageDirectory = join(work, artifact.packagePath)
    runIn('npm', ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], packageDirectory, `${artifact.id} install`)
    // Materialize each built sibling explicitly instead of trusting npm to resolve the manifest's
    // `file:` dependency. That trust already cost one silently wrong build: tsc failed TS2307,
    // and `npm pack` then shipped a stale dist and still exited 0 — a plausible wrong hash rather
    // than an error.
    for (const sibling of siblings) {
      const target = join(packageDirectory, 'node_modules', ...sibling.packageName.split('/'))
      rmSync(target, { recursive: true, force: true })
      mkdirSync(target, { recursive: true })
      cpSync(join(work, sibling.packagePath, 'dist'), join(target, 'dist'), { recursive: true })
      copyFileSync(join(work, sibling.packagePath, 'package.json'), join(target, 'package.json'))
    }
    runIn('npm', ['run', 'build'], packageDirectory, `${artifact.id} build`)
    runIn('npm', ['pack', '--ignore-scripts', '--pack-destination', work], packageDirectory, `${artifact.id} pack`)

    const produced = join(work, artifact.tarball)
    if (!existsSync(produced)) throw new Error(`${artifact.id}: npm pack did not produce ${artifact.tarball}`)
    const actual = sha256File(produced)
    if (artifact.sha256 && actual !== artifact.sha256) {
      throw new Error(`${artifact.id}: rebuild at ${artifact.ref} did not reproduce the pinned artifact.`
        + `\n  expected ${artifact.sha256}\n  actual   ${actual}`
        + `\nThe pin and the ref have diverged; re-pin deliberately rather than relaxing this check.`)
    }
    const destination = join(cacheDirectory, `${actual}.tgz`)
    if (hasVerifiedCacheEntry(artifact, destination, actual)) {
      return { path: destination, sha256: actual }
    }
    const staging = `${destination}.${process.pid}.partial`
    copyFileSync(produced, staging)
    renameSync(staging, destination)
    return { path: destination, sha256: actual }
  } finally {
    disposeSignalCleanup()
  }
}

export function resolveAppshellFeed({ manifestPath = defaultManifestPath, recordBlankPins = false } = {}) {
  // Ticket 289: sweep orphans from a prior killed/failed build before this run mints its own.
  for (const removed of sweepStaleScratchTrees(FEED_SCRATCH_PREFIX)) {
    process.stderr.write(`removed stale gallery feed scratch tree (owner gone, older than 2h): ${removed}\n`)
  }
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'))
  const blankPins = manifest.artifacts.filter(artifact => artifact.sha256 === '')
  if (blankPins.length && !recordBlankPins) {
    throw new Error(`resolve-appshell-feed: blank sha256 pin(s) for ${blankPins.map(artifact => artifact.id).join(', ')}.`
      + ' Blank pins may only be rebuilt with --record-blank-pins.')
  }
  const cacheDirectory = cacheRoot(manifest)
  mkdirSync(cacheDirectory, { recursive: true })
  const ordered = [...manifest.artifacts].sort((left, right) => left.buildOrder - right.buildOrder)
  const environment = {}
  const misses = []
  for (const artifact of ordered) {
    if (artifact.sha256 === '') {
      misses.push(artifact)
      continue
    }
    // An operator-supplied path wins when it is the right artifact, so an existing wiring keeps
    // working. A wrong one self-heals from cache with a notice rather than failing the gate.
    const preset = process.env[artifact.env]
    if (preset && existsSync(preset)) {
      if (sha256File(preset) === artifact.sha256) {
        environment[artifact.env] = resolve(preset)
        continue
      }
      process.stderr.write(`resolve-appshell-feed: ignoring ${artifact.env}=${preset} (sha256 does not match the ${artifact.id} pin)\n`)
    }
    const cached = join(cacheDirectory, `${artifact.sha256}.tgz`)
    if (hasVerifiedCacheEntry(artifact, cached, artifact.sha256)) environment[artifact.env] = cached
    else misses.push(artifact)
  }
  if (misses.length) {
    const repository = sourceRepository(manifest, misses.map(artifact => artifact.ref), cacheDirectory)
    for (const artifact of misses) {
      const built = buildArtifact(artifact, manifest, repository, cacheDirectory)
      environment[artifact.env] = built.path
      if (artifact.sha256 === '') {
        artifact.sha256 = built.sha256
        writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`)
      }
    }
  }
  return environment
}

if (import.meta.main) {
  const args = process.argv.slice(2)
  if (args.some(arg => arg !== '--record-blank-pins') || args.length > 1) {
    throw new Error('usage: resolve-appshell-feed.mjs [--record-blank-pins]')
  }
  const recordBlankPins = args[0] === '--record-blank-pins'
  for (const [name, value] of Object.entries(resolveAppshellFeed({ recordBlankPins }))) process.stdout.write(`${name}=${value}\n`)
}
