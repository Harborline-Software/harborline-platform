import { createHash } from 'node:crypto'
import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'

const safeId = /^[a-z0-9]+(?:[.-][a-z0-9]+)*$/

export function captureFileName(moduleId, scenarioId) {
  if (!safeId.test(moduleId) || !safeId.test(scenarioId)) throw new Error(`unsafe gallery baseline identity: ${moduleId} / ${scenarioId}`)
  return `${moduleId}--${scenarioId}.png`
}

function atomicJson(path, value) {
  mkdirSync(dirname(path), { recursive: true })
  const temporary = `${path}.${process.pid}.tmp`
  writeFileSync(temporary, `${JSON.stringify(value, null, 2)}\n`)
  renameSync(temporary, path)
}

// Pure: the PNG files a set of matrix shards captured, unioned into one capture set. Every shard of a
// recording matrix must have run with --record (a shard that did not carries no `baselineCaptures`
// and its scenarios would silently be absent), each capture must hash to the digest it was shipped
// with, and a file two shards both claim must be byte-identical. Completeness against the catalog is
// NOT decided here: recordGalleryBaselines refuses a missing or unexpected capture exactly as it does
// for an unsharded run, so a lost shard still fails closed at the one place that already owns it.
export function unionShardBaselineCaptures(shardReports) {
  const union = new Map()
  for (const report of shardReports) {
    if (!Array.isArray(report?.baselineCaptures)) {
      throw new Error(`gallery shard ${report?.shard ?? '?'} carries no baseline captures; every shard of a recording matrix must run with --record`)
    }
    for (const capture of report.baselineCaptures) {
      if (!/^[^/\\]+\.png$/.test(capture.fileName ?? '')) throw new Error(`shard ${report.shard} shipped an unsafe capture name ${JSON.stringify(capture.fileName)}`)
      const bytes = Buffer.from(capture.base64 ?? '', 'base64')
      const sha256 = createHash('sha256').update(bytes).digest('hex')
      if (sha256 !== capture.sha256) throw new Error(`shard ${report.shard} capture ${capture.fileName} does not match its digest`)
      const prior = union.get(capture.fileName)
      if (prior && prior.sha256 !== sha256) throw new Error(`shards disagree on ${capture.fileName}: ${prior.sha256} (shard ${prior.shard}) vs ${sha256} (shard ${report.shard})`)
      union.set(capture.fileName, { sha256, bytes, shard: report.shard })
    }
  }
  return union
}

// The producer half of the above: what a recording shard ships, read from its capture directory.
export function readBaselineCaptures(captureRoot) {
  return readdirSync(captureRoot).filter(name => name.endsWith('.png')).sort().map(fileName => {
    const bytes = readFileSync(resolve(captureRoot, fileName))
    return { fileName, sha256: createHash('sha256').update(bytes).digest('hex'), base64: bytes.toString('base64') }
  })
}

export function writeBaselineCaptures(captureRoot, union) {
  mkdirSync(captureRoot, { recursive: true })
  for (const [fileName, { bytes }] of union) writeFileSync(resolve(captureRoot, fileName), bytes)
}

// `additions`, when given, makes the recording ADDITIVE: only those scenarios get an entry, every
// existing entry is kept byte-for-byte, only the manifests that own an addition are rewritten, and no
// stored PNG is deleted. A recording host is not necessarily the host the existing entries were
// recorded on, and re-recording all of them to add one would replace every baseline with this host's
// rasterisation. The capture set must still be COMPLETE -- an addition is only ever recorded from a
// whole passing gate -- and an addition may not overwrite: a scenario that already has an entry, or
// is not in the catalog, is refused.
export function recordGalleryBaselines({ repositoryRoot, captureRoot, catalogs, additions }) {
  const expected = catalogs.flatMap(catalog => catalog.scenarios.map(scenario => ({
    moduleId: catalog.moduleId,
    scenarioId: scenario.id,
    fileName: captureFileName(catalog.moduleId, scenario.id),
  })))
  const expectedNames = new Set(expected.map(item => item.fileName))
  const observedNames = new Set(readdirSync(captureRoot).filter(name => name.endsWith('.png')))
  const missing = [...expectedNames].filter(name => !observedNames.has(name)).sort()
  const unexpected = [...observedNames].filter(name => !expectedNames.has(name)).sort()
  if (missing.length || unexpected.length) {
    throw new Error(`React baseline capture set mismatch; missing=${JSON.stringify(missing)} unexpected=${JSON.stringify(unexpected)}`)
  }

  if (additions) return recordAdditions({ repositoryRoot, captureRoot, catalogs, additions })

  const storeRoot = resolve(repositoryRoot, 'specs/modules/ui/baselines/sha256')
  const byModule = new Map()
  const unique = new Map()
  for (const item of expected) {
    const source = resolve(captureRoot, item.fileName)
    const bytes = readFileSync(source)
    if (bytes.length < 8 || bytes.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a') throw new Error(`${item.fileName} is not a PNG`)
    const sha256 = createHash('sha256').update(bytes).digest('hex')
    const storePath = resolve(storeRoot, `${sha256}.png`)
    mkdirSync(storeRoot, { recursive: true })
    if (!existsSync(storePath)) copyFileSync(source, storePath)
    else if (statSync(storePath).size !== bytes.length || createHash('sha256').update(readFileSync(storePath)).digest('hex') !== sha256) {
      throw new Error(`content-addressed baseline collision at ${storePath}`)
    }
    unique.set(sha256, bytes.length)
    const entries = byModule.get(item.moduleId) ?? []
    entries.push({ scenarioId: item.scenarioId, sha256, bytes: bytes.length, artifact: `../baselines/sha256/${sha256}.png` })
    byModule.set(item.moduleId, entries)
  }

  for (const catalog of catalogs) {
    atomicJson(resolve(repositoryRoot, `specs/modules/ui/${catalog.moduleId}/react-baselines.json`), {
      schemaVersion: 1,
      projection: 'react',
      moduleId: catalog.moduleId,
      scenarios: byModule.get(catalog.moduleId),
    })
  }

  const referenced = new Set([...unique.keys()].map(hash => `${hash}.png`))
  for (const name of readdirSync(storeRoot)) {
    if (name.endsWith('.png') && !referenced.has(name)) rmSync(resolve(storeRoot, name))
  }
  return {
    schemaVersion: 1,
    projection: 'react',
    manifests: catalogs.length,
    scenarios: expected.length,
    uniqueArtifacts: unique.size,
    storedBytes: [...unique.values()].reduce((sum, bytes) => sum + bytes, 0),
    store: 'specs/modules/ui/baselines/sha256',
  }
}

function storeCapture(storeRoot, source, fileName) {
  const bytes = readFileSync(source)
  if (bytes.length < 8 || bytes.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a') throw new Error(`${fileName} is not a PNG`)
  const sha256 = createHash('sha256').update(bytes).digest('hex')
  const storePath = resolve(storeRoot, `${sha256}.png`)
  mkdirSync(storeRoot, { recursive: true })
  if (!existsSync(storePath)) copyFileSync(source, storePath)
  else if (statSync(storePath).size !== bytes.length || createHash('sha256').update(readFileSync(storePath)).digest('hex') !== sha256) {
    throw new Error(`content-addressed baseline collision at ${storePath}`)
  }
  return { sha256, bytes: bytes.length }
}

function recordAdditions({ repositoryRoot, captureRoot, catalogs, additions }) {
  const requested = [...new Set(additions)]
  if (requested.length === 0) throw new Error('additive baseline recording names no scenarios')
  const owner = new Map(catalogs.flatMap(catalog => catalog.scenarios.map((scenario, index) => [scenario.id, { catalog, index }])))
  const unknown = requested.filter(id => !owner.has(id))
  if (unknown.length) throw new Error(`additive baseline recording names scenarios outside the catalog: ${JSON.stringify(unknown)}`)
  const storeRoot = resolve(repositoryRoot, 'specs/modules/ui/baselines/sha256')
  const manifests = new Map()
  const added = []
  for (const scenarioId of requested) {
    const { catalog, index } = owner.get(scenarioId)
    const path = resolve(repositoryRoot, `specs/modules/ui/${catalog.moduleId}/react-baselines.json`)
    const manifest = manifests.get(path)?.manifest
      ?? (existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : { schemaVersion: 1, projection: 'react', moduleId: catalog.moduleId, scenarios: [] })
    if (manifest.scenarios.some(entry => entry.scenarioId === scenarioId)) {
      throw new Error(`additive baseline recording refuses to overwrite the existing ${scenarioId} baseline`)
    }
    const fileName = captureFileName(catalog.moduleId, scenarioId)
    const { sha256, bytes } = storeCapture(storeRoot, resolve(captureRoot, fileName), fileName)
    // Catalog order, without moving an existing entry: after the last entry the catalog places before it.
    const order = id => catalog.scenarios.findIndex(scenario => scenario.id === id)
    let at = 0
    manifest.scenarios.forEach((entry, position) => { if (order(entry.scenarioId) < index) at = position + 1 })
    manifest.scenarios.splice(at, 0, { scenarioId, sha256, bytes, artifact: `../baselines/sha256/${sha256}.png` })
    manifests.set(path, { manifest })
    added.push({ moduleId: catalog.moduleId, scenarioId, sha256, bytes })
  }
  for (const [path, { manifest }] of manifests) atomicJson(path, manifest)
  return {
    schemaVersion: 1,
    projection: 'react',
    mode: 'additive',
    manifests: manifests.size,
    added,
    store: 'specs/modules/ui/baselines/sha256',
  }
}
