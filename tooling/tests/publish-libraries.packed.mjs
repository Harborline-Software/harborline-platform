// Explicit post-pack test: the ordinary tooling suite also runs before any packages exist.
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { readFileSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { inflateRawSync } from 'node:zlib'
import test from 'node:test'
import { computePackageVersion, readPackageVersionProps } from '../package-version.mjs'

const root = resolve(import.meta.dirname, '../..')
function zipEntries(path) {
  const archive = readFileSync(path)
  let end = archive.length - 22
  const earliest = Math.max(0, archive.length - 65557)
  while (end >= earliest && archive.readUInt32LE(end) !== 0x06054b50) end -= 1
  if (end < earliest) throw new Error(`${path}: ZIP end record not found`)
  const entryCount = archive.readUInt16LE(end + 10)
  let cursor = archive.readUInt32LE(end + 16)
  const entries = []
  for (let index = 0; index < entryCount; index += 1) {
    if (archive.readUInt32LE(cursor) !== 0x02014b50) throw new Error(`${path}: invalid ZIP directory`)
    const method = archive.readUInt16LE(cursor + 10)
    const compressedSize = archive.readUInt32LE(cursor + 20)
    const uncompressedSize = archive.readUInt32LE(cursor + 24)
    const nameLength = archive.readUInt16LE(cursor + 28)
    const extraLength = archive.readUInt16LE(cursor + 30)
    const commentLength = archive.readUInt16LE(cursor + 32)
    const localOffset = archive.readUInt32LE(cursor + 42)
    const name = archive.subarray(cursor + 46, cursor + 46 + nameLength).toString('utf8')
    entries.push({ name, method, compressedSize, uncompressedSize, localOffset })
    cursor += 46 + nameLength + extraLength + commentLength
  }
  return {
    entries,
    text(name) {
      const entry = entries.find(candidate => candidate.name === name)
      if (!entry) throw new Error(`${path}: missing ${name}`)
      const nameLength = archive.readUInt16LE(entry.localOffset + 26)
      const extraLength = archive.readUInt16LE(entry.localOffset + 28)
      const start = entry.localOffset + 30 + nameLength + extraLength
      const compressed = archive.subarray(start, start + entry.compressedSize)
      if (entry.method === 0) return compressed.toString('utf8')
      if (entry.method === 8) return inflateRawSync(compressed).toString('utf8')
      throw new Error(`${path}: unsupported ZIP compression ${entry.method}`)
    },
  }
}

function metadata(nuspec, name) {
  return new RegExp(`<${name}(?:\\s[^>]*)?>([^<]+)</${name}>`).exec(nuspec)?.[1]
}


test('all 24 packed nuspecs and manifest hashes agree with the version derived by the consumer pin', () => {
  // eng/platform-pin.json directs the consumer to import this function from its pinned tree.
  const version = computePackageVersion(root)
  assert.equal(readPackageVersionProps(root), version)
  const feed = resolve(root, 'artifacts/packages/nuget')
  const manifest = JSON.parse(readFileSync(resolve(feed, 'manifest.json'), 'utf8'))
  const files = readdirSync(feed).filter(name => name.endsWith('.nupkg')).sort()
  assert.equal(files.length, 24)
  assert.equal(manifest.length, 24)
  assert.equal(new Set(manifest.map(row => row.id)).size, 24)
  assert.deepEqual(files, manifest.map(row => `${row.id}.${row.version}.nupkg`).sort())
  for (const file of files) {
    const path = resolve(feed, file)
    const zip = zipEntries(path)
    const specs = zip.entries.filter(entry => entry.name.endsWith('.nuspec'))
    assert.equal(specs.length, 1)
    const nuspec = zip.text(specs[0].name)
    const id = metadata(nuspec, 'id')
    assert.equal(metadata(nuspec, 'version'), version, `${id}: packed version differs from pin derivation`)
    const sha256 = createHash('sha256').update(readFileSync(path)).digest('hex')
    assert.deepEqual(manifest.find(row => row.id === id), { id, version, sha256 })
    console.log(`${id} ${version} ${sha256}`)
  }
})
