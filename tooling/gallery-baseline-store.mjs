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

export function recordGalleryBaselines({ repositoryRoot, captureRoot, catalogs }) {
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
