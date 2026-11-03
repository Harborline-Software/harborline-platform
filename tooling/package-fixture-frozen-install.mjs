import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { lstatSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { gunzipSync } from 'node:zlib'

const localPackages = [
  ['@harborline-software/ui-react', 'ui-react'],
  ['@harborline-software/contracts', 'contracts'],
  ['@harborline-software/rule-engine', 'rule-engine'],
  ['@harborline-software/rule-authoring', 'rule-authoring'],
]

const scalar = value => value?.startsWith("'") && value.endsWith("'") ? value.slice(1, -1) : value

function blocks(text, indent) {
  const result = new Map()
  const lines = text.replace(/\n+$/, '').split('\n')
  const header = new RegExp(`^ {${indent}}('([^']+)'|[A-Za-z0-9@._/()\\-]+):(?: (.*))?$`)
  for (let index = 0; index < lines.length;) {
    if (lines[index] === '') { index += 1; continue }
    const match = header.exec(lines[index])
    assert.ok(match, `unsupported lock block: ${lines[index]}`)
    const name = scalar(match[1])
    assert.ok(!result.has(name), `duplicate lock key: ${name}`)
    let end = index + 1
    while (end < lines.length && (lines[end] === '' || lines[end].startsWith(' '.repeat(indent + 2)))) end += 1
    const body = lines.slice(index + 1, end).join('\n').replace(/\n+$/, '')
    assert.ok(match[3] === undefined || body === '', `mixed scalar and mapping: ${name}`)
    result.set(name, { value: scalar(match[3]), body, raw: lines.slice(index, end).join('\n').replace(/\n+$/, '') })
    index = end
  }
  return result
}

function sameKeys(actual, expected, label) {
  assert.deepEqual([...actual].sort(), [...expected].sort(), label)
}

function values(text, indent) {
  return Object.fromEntries([...blocks(text, indent)].map(([name, record]) => {
    assert.ok(record.value !== undefined && record.body === '', `expected scalar: ${name}`)
    return [name, record.value]
  }))
}

function lock(text) {
  assert.ok(typeof text === 'string' && text.endsWith('\n') && !/[\r\t]/.test(text), 'lock must be canonical LF text')
  const top = blocks(text, 0)
  const empty = !top.has('packages') && !top.has('snapshots')
  sameKeys(top.keys(), ['lockfileVersion', 'settings', 'importers', ...(empty ? [] : ['packages', 'snapshots'])], 'unknown lock section')
  assert.equal(top.get('lockfileVersion').value, '9.0', 'unsupported lock version')
  for (const name of ['settings', 'importers', ...(empty ? [] : ['packages', 'snapshots'])]) assert.equal(top.get(name).value, undefined)
  const importers = blocks(top.get('importers').body, 2)
  sameKeys(importers.keys(), ['.'], 'only the consumer importer is permitted')
  if (importers.get('.').value === '{}') {
    assert.equal(importers.get('.').body, '', 'empty importer has no body')
    return { top, dependencies: new Map(), pins: new Map(), packages: blocks(top.get('packages')?.body ?? '', 2), snapshots: blocks(top.get('snapshots')?.body ?? '', 2) }
  }
  assert.equal(importers.get('.').value, undefined)
  const root = blocks(importers.get('.').body, 4)
  sameKeys(root.keys(), ['dependencies'], 'unsupported importer shape')
  assert.equal(root.get('dependencies').value, undefined)
  const dependencies = blocks(root.get('dependencies').body, 6)
  const pins = new Map([...dependencies].map(([name, record]) => {
    assert.equal(record.value, undefined)
    const fields = values(record.body, 8)
    sameKeys(Object.keys(fields), ['specifier', 'version'], `unsupported importer dependency: ${name}`)
    return [name, fields]
  }))
  return { top, dependencies, pins, packages: blocks(top.get('packages').body, 2), snapshots: blocks(top.get('snapshots').body, 2) }
}

function packedManifest(bytes) {
  const tar = gunzipSync(bytes, { maxOutputLength: 8 * 1024 * 1024 })
  let manifest
  for (let offset = 0; offset < tar.length;) {
    assert.ok(offset + 512 <= tar.length, 'truncated packed header')
    const header = tar.subarray(offset, offset + 512)
    if (header.every(byte => byte === 0)) {
      assert.ok(tar.subarray(offset).every(byte => byte === 0), 'trailing packed data')
      break
    }
    const string = (start, end) => header.subarray(start, end).toString('utf8').replace(/\0.*$/, '')
    const sizeText = string(124, 136).trim()
    const sumText = string(148, 156).trim()
    assert.match(sizeText, /^[0-7]+$/, 'unsupported packed size')
    assert.match(sumText, /^[0-7]+$/, 'unsupported packed checksum')
    const sum = header.reduce((total, byte, index) => total + (index >= 148 && index < 156 ? 32 : byte), 0)
    assert.equal(sum, Number.parseInt(sumText, 8), 'packed header checksum')
    const size = Number.parseInt(sizeText, 8)
    const end = offset + 512 + size
    assert.ok(end <= tar.length, 'truncated packed entry')
    const type = string(156, 157)
    assert.ok(['', '0', '5'].includes(type), 'unsupported packed entry type')
    const name = [string(345, 500), string(0, 100)].filter(Boolean).join('/')
    if (name === 'package/package.json') {
      assert.ok(!manifest && type !== '5', 'duplicate or invalid packed manifest')
      manifest = JSON.parse(tar.subarray(offset + 512, end).toString('utf8'))
    }
    offset += 512 + Math.ceil(size / 512) * 512
  }
  assert.ok(manifest && typeof manifest.version === 'string', 'packed manifest missing identity')
  return manifest
}

function peerContext(reference, prefix) {
  assert.ok(reference.startsWith(prefix), 'local reference changed its tarball path')
  const result = new Map()
  let suffix = reference.slice(prefix.length)
  while (suffix) {
    assert.equal(suffix[0], '(', 'malformed local peer context')
    let depth = 1
    let end = 1
    for (; end < suffix.length && depth; end += 1) {
      if (suffix[end] === '(') depth += 1
      if (suffix[end] === ')') depth -= 1
    }
    assert.equal(depth, 0, 'unterminated local peer context')
    const match = /^(@[^/]+\/[^@]+|[^@]+)@(.+)$/.exec(suffix.slice(1, end - 1))
    assert.ok(match && !result.has(match[1]), 'duplicate or malformed local peer')
    result.set(match[1], match[2])
    suffix = suffix.slice(end)
  }
  return result
}

export function assertFrozenConsumerGraph(baselineText, derivedText, artifacts) {
  const baseline = lock(baselineText)
  const derived = lock(derivedText)
  const baselineNames = [...baseline.pins.keys()]
  sameKeys(derived.pins.keys(), [...baselineNames, ...artifacts.map(item => item.name)], 'unexpected importer dependency')
  for (const section of ['lockfileVersion', 'settings']) {
    assert.equal(derived.top.get(section).raw, baseline.top.get(section).raw, `${section} changed`)
  }
  for (const name of baselineNames) {
    assert.equal(derived.dependencies.get(name).raw, baseline.dependencies.get(name).raw, `external importer changed: ${name}`)
  }
  const references = new Map(baseline.pins)
  for (const artifact of artifacts) {
    const pin = derived.pins.get(artifact.name)
    assert.equal(pin.specifier.replaceAll('\\', '/'), artifact.spec, `local specifier changed: ${artifact.name}`)
    peerContext(pin.version, artifact.spec)
    references.set(artifact.name, pin)
  }
  const packageKeys = artifacts.map(item => `${item.name}@${item.spec}`)
  const snapshotKeys = artifacts.map(item => `${item.name}@${derived.pins.get(item.name).version}`)
  for (const [section, localKeys] of [['packages', packageKeys], ['snapshots', snapshotKeys]]) {
    sameKeys(derived[section].keys(), [...baseline[section].keys(), ...localKeys], `unexpected ${section} entry`)
    for (const [name, record] of baseline[section]) {
      assert.equal(derived[section].get(name).raw, record.raw, `external ${section} changed: ${name}`)
    }
  }
  for (const [index, artifact] of artifacts.entries()) {
    const { manifest } = artifact
    const entry = derived.packages.get(packageKeys[index])
    assert.equal(entry.value, undefined)
    const fields = blocks(entry.body, 4)
    const optionalFields = ['peerDependencies', 'peerDependenciesMeta'].filter(name => Object.keys(manifest[name] ?? {}).length)
    sameKeys(fields.keys(), ['resolution', 'version', ...optionalFields], `unsupported local package: ${artifact.name}`)
    assert.equal(fields.get('resolution').value, `{integrity: ${artifact.integrity}, tarball: ${artifact.spec}}`, 'local tarball integrity/path mismatch')
    assert.equal(fields.get('version').value, manifest.version, 'local package version mismatch')
    if (fields.has('peerDependencies')) assert.deepEqual(values(fields.get('peerDependencies').body, 6), manifest.peerDependencies, 'local peer declarations changed')
    if (fields.has('peerDependenciesMeta')) {
      const meta = blocks(fields.get('peerDependenciesMeta').body, 6)
      sameKeys(meta.keys(), Object.keys(manifest.peerDependenciesMeta), 'local peer metadata changed')
      for (const [name, record] of meta) assert.deepEqual(values(record.body, 8), { optional: String(manifest.peerDependenciesMeta[name].optional) }, 'unsupported peer metadata')
    }
    const snapshot = derived.snapshots.get(snapshotKeys[index])
    assert.ok(snapshot.value === undefined || snapshot.value === '{}', 'unsupported local snapshot')
    const edges = blocks(snapshot.body, 4)
    assert.ok([...edges.keys()].every(name => ['dependencies', 'optionalDependencies', 'transitivePeerDependencies'].includes(name)), 'unsupported local snapshot field')
    const actual = {}
    for (const kind of ['dependencies', 'optionalDependencies']) {
      if (!edges.has(kind)) continue
      assert.equal(edges.get(kind).value, undefined)
      for (const [name, version] of Object.entries(values(edges.get(kind).body, 6))) {
        assert.ok(!Object.hasOwn(actual, name), 'duplicate local dependency edge')
        const optional = Object.hasOwn(manifest.optionalDependencies ?? {}, name) || manifest.peerDependenciesMeta?.[name]?.optional === true
        assert.equal(kind, optional ? 'optionalDependencies' : 'dependencies', 'local dependency optionality changed')
        actual[name] = version
      }
    }
    const wanted = { ...manifest.dependencies, ...manifest.optionalDependencies, ...manifest.peerDependencies }
    sameKeys(Object.keys(actual), Object.keys(wanted), 'local dependency edges changed')
    for (const [name, version] of Object.entries(actual)) {
      assert.ok(references.has(name), `unbound local dependency edge: ${name}`)
      assert.equal(version, references.get(name).version, `local dependency target changed: ${name}`)
      if (Object.hasOwn(manifest.dependencies ?? {}, name)) assert.equal(wanted[name], references.get(name).specifier, 'local runtime dependency pin changed')
    }
    const transitive = edges.has('transitivePeerDependencies') ? edges.get('transitivePeerDependencies').body.split('\n').map(line => {
      const match = /^ {6}- ('[^']+'|[A-Za-z0-9@._/\-]+)$/.exec(line)
      assert.ok(match, 'unsupported transitive peer list')
      return scalar(match[1])
    }) : []
    sameKeys(new Set(transitive), transitive, 'duplicate transitive peer')
    const expectedTransitive = new Set()
    for (const [name, version] of Object.entries(manifest.dependencies ?? {})) {
      const external = baseline.packages.get(`${name}@${version}`)
      assert.ok(external, `unsupported runtime dependency: ${name}`)
      const peerDeclarations = blocks(external.body, 4).get('peerDependencies')
      if (peerDeclarations) {
        for (const peer of Object.keys(values(peerDeclarations.body, 6))) {
          if (!Object.hasOwn(wanted, peer)) expectedTransitive.add(peer)
        }
      }
    }
    sameKeys(transitive, expectedTransitive, 'transitive peer declarations changed')
    const peers = peerContext(derived.pins.get(artifact.name).version, artifact.spec)
    sameKeys(peers.keys(), [...Object.keys(manifest.peerDependencies ?? {}), ...transitive], 'local peer context changed')
    for (const [name, version] of peers) assert.equal(version, references.get(name)?.version, `local peer context target changed: ${name}`)
  }
}

export function installFrozenUiConsumer(consumer, paths, run) {
  assert.equal(paths.length, localPackages.length, 'exactly four packed artifacts required')
  const manifestPath = resolve(consumer, 'package.json')
  const lockPath = resolve(consumer, 'pnpm-lock.yaml')
  const workspacePath = resolve(consumer, 'pnpm-workspace.yaml')
  const manifestText = readFileSync(manifestPath, 'utf8')
  const manifest = JSON.parse(manifestText)
  const baselineText = readFileSync(lockPath, 'utf8')
  const baseline = lock(baselineText)
  const workspace = readFileSync(workspacePath, 'utf8')
  assert.equal(workspace, 'packages:\n  - .\nnodeLinker: hoisted\nautoInstallPeers: false\n', 'consumer workspace changed')
  assert.equal(manifest.packageManager, 'pnpm@11.1.3', 'consumer pnpm pin changed')
  sameKeys(baseline.pins.keys(), Object.keys(manifest.dependencies), 'committed manifest/lock mismatch')
  for (const [name, version] of Object.entries(manifest.dependencies)) assert.equal(baseline.pins.get(name).specifier, version, 'committed dependency pin mismatch')
  run('pnpm', ['install', '--frozen-lockfile', '--ignore-scripts'], { cwd: consumer })
  assert.equal(readFileSync(lockPath, 'utf8'), baselineText, 'baseline install rewrote committed lock')
  assert.equal(readFileSync(manifestPath, 'utf8'), manifestText, 'baseline install rewrote committed manifest')
  mkdirSync(resolve(consumer, '.packed'))
  const artifacts = paths.map((path, index) => {
    assert.ok(lstatSync(path).isFile(), 'packed artifact must be a regular file')
    const bytes = readFileSync(path)
    const [name, file] = localPackages[index]
    const packed = packedManifest(bytes)
    assert.equal(packed.name, name, 'packed artifact identity mismatch')
    const spec = `file:.packed/${file}.tgz`
    writeFileSync(resolve(consumer, '.packed', `${file}.tgz`), bytes, { flag: 'wx' })
    return { name, spec, manifest: packed, bytes, path, integrity: `sha512-${createHash('sha512').update(bytes).digest('base64')}` }
  })
  assert.deepEqual(artifacts[2].manifest.peerDependencies, { [artifacts[1].name]: artifacts[1].manifest.version }, 'UI runtime must declare exactly the selected Contracts peer')
  assert.deepEqual(artifacts[2].manifest.peerDependenciesMeta === undefined ? {} : artifacts[2].manifest.peerDependenciesMeta, {}, 'UI runtime Contracts peer must be required')
  run('pnpm', ['add', '--lockfile-only', '--offline', '--ignore-scripts', ...artifacts.map(item => item.spec)], { cwd: consumer })
  const derivedText = readFileSync(lockPath, 'utf8')
  const derivedManifestText = readFileSync(manifestPath, 'utf8')
  const derivedManifest = JSON.parse(derivedManifestText)
  for (const { name } of artifacts) {
    if (typeof derivedManifest.dependencies?.[name] === 'string') derivedManifest.dependencies[name] = derivedManifest.dependencies[name].replaceAll('\\', '/')
  }
  assert.deepEqual(derivedManifest, { ...manifest, dependencies: { ...manifest.dependencies, ...Object.fromEntries(artifacts.map(item => [item.name, item.spec])) } }, 'derived manifest changed external inputs')
  assertFrozenConsumerGraph(baselineText, derivedText, artifacts)
  const unchanged = () => {
    assert.equal(readFileSync(workspacePath, 'utf8'), workspace, 'workspace changed during local derivation')
    for (const item of artifacts) {
      assert.deepEqual(readFileSync(item.path), item.bytes, 'producer tarball bytes changed')
      assert.deepEqual(readFileSync(resolve(consumer, item.spec.slice(5))), item.bytes, 'staged tarball bytes changed')
    }
  }
  unchanged()
  run('pnpm', ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], { cwd: consumer })
  assert.equal(readFileSync(lockPath, 'utf8'), derivedText, 'final install rewrote checked lock')
  assert.equal(readFileSync(manifestPath, 'utf8'), derivedManifestText, 'final install rewrote checked manifest')
  unchanged()
}


export function installFrozenContractsConsumer(consumer, path, run) {
  const name = '@harborline-software/contracts'
  const spec = 'file:.packed/contracts.tgz'
  const manifestPath = resolve(consumer, 'package.json')
  const lockPath = resolve(consumer, 'pnpm-lock.yaml')
  const workspacePath = resolve(consumer, 'pnpm-workspace.yaml')
  const manifestText = readFileSync(manifestPath, 'utf8')
  const manifest = JSON.parse(manifestText)
  const baselineText = readFileSync(lockPath, 'utf8')
  const workspace = readFileSync(workspacePath, 'utf8')
  assert.deepEqual(manifest, { private: true, type: 'module', packageManager: 'pnpm@11.1.3' }, 'Contracts consumer manifest changed')
  assert.equal(createHash('sha256').update(baselineText).digest('hex'), '878a7b60746fb21ffe11029327f29ea50cfc840bfeb9e0b3cbb46a77f828b7e7', 'Contracts empty baseline changed')
  assert.equal(workspace, 'packages:\n  - .\nnodeLinker: hoisted\nautoInstallPeers: false\n', 'Contracts consumer workspace changed')
  run('pnpm', ['install', '--frozen-lockfile', '--ignore-scripts'], { cwd: consumer })
  assert.equal(readFileSync(lockPath, 'utf8'), baselineText, 'Contracts baseline install rewrote lock')
  assert.equal(readFileSync(manifestPath, 'utf8'), manifestText, 'Contracts baseline install rewrote manifest')
  assert.equal(readFileSync(workspacePath, 'utf8'), workspace, 'Contracts baseline install rewrote workspace')
  assert.ok(lstatSync(path).isFile(), 'Contracts packed artifact must be a regular file')
  const bytes = readFileSync(path)
  const packed = packedManifest(bytes)
  assert.equal(packed.name, name, 'Contracts packed artifact identity mismatch')
  assert.match(packed.version, /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/, 'unsupported Contracts package version')
  for (const field of ['dependencies', 'optionalDependencies', 'peerDependencies', 'peerDependenciesMeta']) {
    assert.deepEqual(packed[field] === undefined ? {} : packed[field], {}, `Contracts packed ${field} must be empty`)
  }
  const integrity = `sha512-${createHash('sha512').update(bytes).digest('base64')}`
  mkdirSync(resolve(consumer, '.packed'))
  const staged = resolve(consumer, '.packed/contracts.tgz')
  writeFileSync(staged, bytes, { flag: 'wx' })
  run('pnpm', ['add', '--lockfile-only', '--offline', '--ignore-scripts', spec], { cwd: consumer })
  const derivedText = readFileSync(lockPath, 'utf8')
  const derivedManifestText = readFileSync(manifestPath, 'utf8')
  const derivedManifest = JSON.parse(derivedManifestText)
  if (typeof derivedManifest.dependencies?.[name] === 'string') derivedManifest.dependencies[name] = derivedManifest.dependencies[name].replaceAll('\\', '/')
  assert.deepEqual(derivedManifest, { ...manifest, dependencies: { [name]: spec } }, 'Contracts derived manifest changed')
  // Actual pnpm 11.1.3 singleton shape; any additional closure needs separate admission.
  const expected = baselineText.replace('  .: {}\n', `  .:
    dependencies:
      '${name}':
        specifier: ${spec}
        version: ${spec}
`) + `
packages:

  '${name}@${spec}':
    resolution: {integrity: ${integrity}, tarball: ${spec}}
    version: ${packed.version}

snapshots:

  '${name}@${spec}': {}
`
  assert.equal(derivedText.replace('specifier: file:.packed\\contracts.tgz', 'specifier: file:.packed/contracts.tgz'), expected, 'Contracts singleton lock graph changed')
  const unchanged = () => {
    assert.equal(readFileSync(workspacePath, 'utf8'), workspace, 'Contracts workspace changed')
    assert.deepEqual(readFileSync(path), bytes, 'Contracts producer tarball changed')
    assert.deepEqual(readFileSync(staged), bytes, 'Contracts staged tarball changed')
  }
  unchanged()
  run('pnpm', ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], { cwd: consumer })
  assert.equal(readFileSync(lockPath, 'utf8'), derivedText, 'Contracts final install rewrote lock')
  assert.equal(readFileSync(manifestPath, 'utf8'), derivedManifestText, 'Contracts final install rewrote manifest')
  unchanged()
}

export function installFrozenRuleRuntimeConsumer(consumer, enginePath, contractsPath, run) {
  const name = '@harborline-software/rule-engine'
  const spec = 'file:.packed/rule-engine.tgz'
  const contractsName = '@harborline-software/contracts'
  const contractsSpec = 'file:.packed/contracts.tgz'
  const manifestPath = resolve(consumer, 'package.json')
  const lockPath = resolve(consumer, 'pnpm-lock.yaml')
  const workspacePath = resolve(consumer, 'pnpm-workspace.yaml')
  const manifestText = readFileSync(manifestPath, 'utf8')
  const manifest = JSON.parse(manifestText)
  const baselineText = readFileSync(lockPath, 'utf8')
  const workspace = readFileSync(workspacePath, 'utf8')
  assert.deepEqual(manifest, { private: true, type: 'module', packageManager: 'pnpm@11.1.3' }, 'Rule Runtime consumer manifest changed')
  assert.equal(createHash('sha256').update(baselineText).digest('hex'), '878a7b60746fb21ffe11029327f29ea50cfc840bfeb9e0b3cbb46a77f828b7e7', 'Rule Runtime empty baseline changed')
  assert.equal(workspace, 'packages:\n  - .\nnodeLinker: hoisted\nautoInstallPeers: false\n', 'Rule Runtime consumer workspace changed')
  run('pnpm', ['install', '--frozen-lockfile', '--ignore-scripts'], { cwd: consumer })
  assert.equal(readFileSync(lockPath, 'utf8'), baselineText, 'Rule Runtime baseline install rewrote lock')
  assert.equal(readFileSync(manifestPath, 'utf8'), manifestText, 'Rule Runtime baseline install rewrote manifest')
  assert.equal(readFileSync(workspacePath, 'utf8'), workspace, 'Rule Runtime baseline install rewrote workspace')
  const artifacts = [[enginePath, name, spec], [contractsPath, contractsName, contractsSpec]].map(([path, name, spec]) => {
    assert.ok(lstatSync(path).isFile(), 'Rule Runtime packed artifact must be a regular file')
    const bytes = readFileSync(path)
    const packed = packedManifest(bytes)
    assert.equal(packed.name, name, 'Rule Runtime packed artifact identity mismatch')
    assert.match(packed.version, /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/, 'unsupported Rule Runtime package version')
    for (const field of ['dependencies', 'optionalDependencies']) {
      assert.deepEqual(packed[field] === undefined ? {} : packed[field], {}, `Rule Runtime packed ${field} must be empty`)
    }
    return { name, path, spec, bytes, manifest: packed, integrity: `sha512-${createHash('sha512').update(bytes).digest('base64')}` }
  })
  const [engine, contracts] = artifacts
  assert.deepEqual(engine.manifest.peerDependencies, { [contractsName]: contracts.manifest.version }, 'Rule Runtime must declare exactly the selected Contracts peer')
  assert.deepEqual(engine.manifest.peerDependenciesMeta === undefined ? {} : engine.manifest.peerDependenciesMeta, {}, 'Rule Runtime Contracts peer must be required')
  for (const field of ['peerDependencies', 'peerDependenciesMeta']) {
    assert.deepEqual(contracts.manifest[field] === undefined ? {} : contracts.manifest[field], {}, `Rule Runtime Contracts ${field} must be empty`)
  }
  mkdirSync(resolve(consumer, '.packed'))
  for (const item of artifacts) writeFileSync(resolve(consumer, item.spec.slice(5)), item.bytes, { flag: 'wx' })
  run('pnpm', ['add', '--lockfile-only', '--offline', '--ignore-scripts', spec, contractsSpec], { cwd: consumer })
  const derivedText = readFileSync(lockPath, 'utf8')
  const derivedManifestText = readFileSync(manifestPath, 'utf8')
  const derivedManifest = JSON.parse(derivedManifestText)
  for (const { name } of artifacts) {
    if (typeof derivedManifest.dependencies?.[name] === 'string') derivedManifest.dependencies[name] = derivedManifest.dependencies[name].replaceAll('\\', '/')
  }
  assert.deepEqual(derivedManifest, { ...manifest, dependencies: { [name]: spec, [contractsName]: contractsSpec } }, 'Rule Runtime derived manifest changed')
  assertFrozenConsumerGraph(baselineText, derivedText, artifacts)
  const unchanged = () => {
    assert.equal(readFileSync(workspacePath, 'utf8'), workspace, 'Rule Runtime workspace changed')
    for (const item of artifacts) {
      assert.deepEqual(readFileSync(item.path), item.bytes, 'Rule Runtime producer tarball changed')
      assert.deepEqual(readFileSync(resolve(consumer, item.spec.slice(5))), item.bytes, 'Rule Runtime staged tarball changed')
    }
  }
  unchanged()
  run('pnpm', ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], { cwd: consumer })
  assert.equal(readFileSync(lockPath, 'utf8'), derivedText, 'Rule Runtime final install rewrote lock')
  assert.equal(readFileSync(manifestPath, 'utf8'), derivedManifestText, 'Rule Runtime final install rewrote manifest')
  unchanged()
}

export function installFrozenRuleAuthoringConsumer(consumer, authoringPath, enginePath, contractsPath, run) {
  const authoringName = '@harborline-software/rule-authoring'
  const engineName = '@harborline-software/rule-engine'
  const authoringSpec = 'file:.packed/rule-authoring.tgz'
  const engineSpec = 'file:.packed/rule-engine.tgz'
  const contractsName = '@harborline-software/contracts'
  const contractsSpec = 'file:.packed/contracts.tgz'
  const manifestPath = resolve(consumer, 'package.json')
  const lockPath = resolve(consumer, 'pnpm-lock.yaml')
  const workspacePath = resolve(consumer, 'pnpm-workspace.yaml')
  const manifestText = readFileSync(manifestPath, 'utf8')
  const manifest = JSON.parse(manifestText)
  const baselineText = readFileSync(lockPath, 'utf8')
  const workspace = readFileSync(workspacePath, 'utf8')
  assert.deepEqual(manifest, { private: true, type: 'module', packageManager: 'pnpm@11.1.3' }, 'Rule Authoring consumer manifest changed')
  assert.equal(createHash('sha256').update(baselineText).digest('hex'), '878a7b60746fb21ffe11029327f29ea50cfc840bfeb9e0b3cbb46a77f828b7e7', 'Rule Authoring empty baseline changed')
  assert.equal(workspace, 'packages:\n  - .\nnodeLinker: hoisted\nautoInstallPeers: false\n', 'Rule Authoring consumer workspace changed')
  run('pnpm', ['install', '--frozen-lockfile', '--ignore-scripts'], { cwd: consumer })
  assert.equal(readFileSync(lockPath, 'utf8'), baselineText, 'Rule Authoring baseline install rewrote lock')
  assert.equal(readFileSync(manifestPath, 'utf8'), manifestText, 'Rule Authoring baseline install rewrote manifest')
  assert.equal(readFileSync(workspacePath, 'utf8'), workspace, 'Rule Authoring baseline install rewrote workspace')
  const artifacts = [[authoringPath, authoringName, authoringSpec], [enginePath, engineName, engineSpec], [contractsPath, contractsName, contractsSpec]].map(([path, name, spec]) => {
    assert.ok(lstatSync(path).isFile(), 'Rule Authoring packed artifact must be a regular file')
    const bytes = readFileSync(path)
    const packed = packedManifest(bytes)
    assert.equal(packed.name, name, 'Rule Authoring packed artifact identity mismatch')
    assert.match(packed.version, /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/, 'unsupported Rule Authoring package version')
    for (const field of ['dependencies', 'optionalDependencies']) {
      assert.deepEqual(packed[field] === undefined ? {} : packed[field], {}, `Rule Authoring packed ${field} must be empty`)
    }
    return { name, path, spec, bytes, manifest: packed, integrity: `sha512-${createHash('sha512').update(bytes).digest('base64')}` }
  })
  const [authoring, engine, contracts] = artifacts
  assert.deepEqual(authoring.manifest.peerDependencies, { [engineName]: engine.manifest.version }, 'Rule Authoring must declare exactly the selected runtime peer')
  assert.deepEqual(authoring.manifest.peerDependenciesMeta, { [engineName]: { optional: true } }, 'Rule Authoring runtime peer must be exactly optional')
  assert.deepEqual(engine.manifest.peerDependencies, { [contractsName]: contracts.manifest.version }, 'Rule Authoring runtime must declare exactly the selected Contracts peer')
  assert.deepEqual(engine.manifest.peerDependenciesMeta === undefined ? {} : engine.manifest.peerDependenciesMeta, {}, 'Rule Authoring runtime Contracts peer must be required')
  for (const field of ['peerDependencies', 'peerDependenciesMeta']) {
    assert.deepEqual(contracts.manifest[field] === undefined ? {} : contracts.manifest[field], {}, `Rule Authoring Contracts ${field} must be empty`)
  }
  mkdirSync(resolve(consumer, '.packed'))
  for (const item of artifacts) writeFileSync(resolve(consumer, item.spec.slice(5)), item.bytes, { flag: 'wx' })
  run('pnpm', ['add', '--lockfile-only', '--offline', '--ignore-scripts', authoringSpec, engineSpec, contractsSpec], { cwd: consumer })
  const derivedText = readFileSync(lockPath, 'utf8')
  const derivedManifestText = readFileSync(manifestPath, 'utf8')
  const derivedManifest = JSON.parse(derivedManifestText)
  for (const { name } of artifacts) {
    if (typeof derivedManifest.dependencies?.[name] === 'string') derivedManifest.dependencies[name] = derivedManifest.dependencies[name].replaceAll('\\', '/')
  }
  assert.deepEqual(derivedManifest, { ...manifest, dependencies: { [authoringName]: authoringSpec, [engineName]: engineSpec, [contractsName]: contractsSpec } }, 'Rule Authoring derived manifest changed')
  assertFrozenConsumerGraph(baselineText, derivedText, artifacts)
  const unchanged = () => {
    assert.equal(readFileSync(workspacePath, 'utf8'), workspace, 'Rule Authoring workspace changed')
    for (const item of artifacts) {
      assert.deepEqual(readFileSync(item.path), item.bytes, 'Rule Authoring producer tarball changed')
      assert.deepEqual(readFileSync(resolve(consumer, item.spec.slice(5))), item.bytes, 'Rule Authoring staged tarball changed')
    }
  }
  unchanged()
  run('pnpm', ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], { cwd: consumer })
  assert.equal(readFileSync(lockPath, 'utf8'), derivedText, 'Rule Authoring final install rewrote lock')
  assert.equal(readFileSync(manifestPath, 'utf8'), derivedManifestText, 'Rule Authoring final install rewrote manifest')
  unchanged()
}
