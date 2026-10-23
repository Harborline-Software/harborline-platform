import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { resolve } from 'node:path'
import test from 'node:test'
import { gunzipSync, gzipSync } from 'node:zlib'

import { installFrozenUiConsumer } from '../package-fixture-frozen-install.mjs'

const root = resolve(import.meta.dirname, '../..')
const fixture = resolve(root, 'tests/package-consumers/npm')
const baseline = readFileSync(resolve(fixture, 'pnpm-lock.yaml'), 'utf8')
const baselineManifest = JSON.parse(readFileSync(resolve(fixture, 'package.json'), 'utf8'))
const dynamicFixture = resolve(root, 'tests/package-consumers/dynamic-forms-npm')
const dynamicBaseline = readFileSync(resolve(dynamicFixture, 'pnpm-lock.yaml'), 'utf8')
const dynamicManifest = JSON.parse(readFileSync(resolve(dynamicFixture, 'package.json'), 'utf8'))
const names = ['ui-react', 'contracts', 'rule-engine', 'rule-authoring']
const ids = names.map(name => `@harborline-software/${name}`)
const specs = names.map(name => `file:.packed/${name}.tgz`)
const engineReference = `${specs[2]}(@harborline-software/contracts@${specs[1]})`
const authoringReference = `${specs[3]}(@harborline-software/rule-engine@${engineReference})`
const uiReference = `${specs[0]}(@harborline-software/contracts@${specs[1]})(@harborline-software/rule-authoring@${authoringReference})(@harborline-software/rule-engine@${engineReference})(@types/react@19.2.18)(react-dom@19.2.8(react@19.2.8))(react@19.2.8)`
const references = [uiReference, specs[1], engineReference, authoringReference]
const manifests = [
  {
    name: ids[0], version: '0.8.0-alpha',
    dependencies: { '@radix-ui/react-slot': '1.3.3', clsx: '2.1.1', 'tailwind-merge': '3.6.0' },
    peerDependencies: {
      '@harborline-software/contracts': '0.0.0-alpha.0',
      '@harborline-software/rule-authoring': '0.1.0-alpha.0',
      '@harborline-software/rule-engine': '0.1.0-alpha.0',
      react: '^18.0.0 || ^19.0.0', 'react-dom': '^18.0.0 || ^19.0.0',
    },
    peerDependenciesMeta: {
      '@harborline-software/contracts': { optional: true },
      '@harborline-software/rule-authoring': { optional: true },
      '@harborline-software/rule-engine': { optional: true },
    },
  },
  { name: ids[1], version: '0.0.0-alpha.0' },
  { name: ids[2], version: '0.1.0-alpha.0', peerDependencies: { '@harborline-software/contracts': '0.0.0-alpha.0' } },
  {
    name: ids[3], version: '0.1.0-alpha.0',
    peerDependencies: { '@harborline-software/rule-engine': '0.1.0-alpha.0' },
    peerDependenciesMeta: { '@harborline-software/rule-engine': { optional: true } },
  },
]

function archive(manifest) {
  const json = Buffer.from(JSON.stringify(manifest))
  const header = Buffer.alloc(512)
  header.write('package/package.json')
  for (const offset of [100, 108, 116]) header.write('0000644\0', offset)
  header.write(`${json.length.toString(8).padStart(11, '0')}\0`, 124)
  header.write('00000000000\0', 136)
  header.fill(32, 148, 156)
  header.write('0', 156)
  header.write('ustar\0', 257)
  const checksum = header.reduce((sum, byte) => sum + byte, 0)
  header.write(`${checksum.toString(8).padStart(6, '0')}\0 `, 148)
  const payload = Buffer.alloc(Math.ceil(json.length / 512) * 512)
  json.copy(payload)
  return gzipSync(Buffer.concat([header, payload, Buffer.alloc(1024)]))
}

function derivedLock(bytes, externalBaseline = baseline) {
  const importer = ids.map((name, index) => `      '${name}':\n        specifier: ${specs[index]}\n        version: ${references[index]}\n`).join('')
  const packages = ids.map((name, index) => {
    const integrity = `sha512-${createHash('sha512').update(bytes[index]).digest('base64')}`
    let text = `  '${name}@${specs[index]}':\n    resolution: {integrity: ${integrity}, tarball: ${specs[index]}}\n    version: ${manifests[index].version}\n`
    if (index === 0) text += `    peerDependencies:\n      '@harborline-software/contracts': 0.0.0-alpha.0\n      '@harborline-software/rule-authoring': 0.1.0-alpha.0\n      '@harborline-software/rule-engine': 0.1.0-alpha.0\n      react: ^18.0.0 || ^19.0.0\n      react-dom: ^18.0.0 || ^19.0.0\n    peerDependenciesMeta:\n      '@harborline-software/contracts':\n        optional: true\n      '@harborline-software/rule-authoring':\n        optional: true\n      '@harborline-software/rule-engine':\n        optional: true\n`
    if (index === 2) text += `    peerDependencies:\n      '@harborline-software/contracts': 0.0.0-alpha.0\n`
    if (index === 3) text += `    peerDependencies:\n      '@harborline-software/rule-engine': 0.1.0-alpha.0\n    peerDependenciesMeta:\n      '@harborline-software/rule-engine':\n        optional: true\n`
    return `${text}\n`
  }).join('')
  const snapshots = `  '${ids[0]}@${references[0]}':
    dependencies:
      '@radix-ui/react-slot': 1.3.3(@types/react@19.2.18)(react@19.2.8)
      clsx: 2.1.1
      react: 19.2.8
      react-dom: 19.2.8(react@19.2.8)
      tailwind-merge: 3.6.0
    optionalDependencies:
      '@harborline-software/contracts': ${specs[1]}
      '@harborline-software/rule-authoring': ${references[3]}
      '@harborline-software/rule-engine': ${engineReference}
    transitivePeerDependencies:
      - '@types/react'

  '${ids[1]}@${references[1]}': {}

  '${ids[2]}@${references[2]}':
    dependencies:
      '@harborline-software/contracts': ${specs[1]}

  '${ids[3]}@${references[3]}':
    optionalDependencies:
      '@harborline-software/rule-engine': ${engineReference}

`
  return externalBaseline.replace('    dependencies:\n', `    dependencies:\n${importer}`)
    .replace('packages:\n\n', `packages:\n\n${packages}`)
    .replace('snapshots:\n\n', `snapshots:\n\n${snapshots}`)
}

function stage(t, { fixturePath = fixture, mutateLock = text => text, mutateManifest = value => value, failAt, afterCall = () => {} } = {}) {
  const scratch = mkdtempSync(resolve(tmpdir(), 'harborline-frozen-consumer-'))
  t.after(() => rmSync(scratch, { recursive: true, force: true }))
  const consumer = resolve(scratch, 'consumer')
  const externalBaseline = readFileSync(resolve(fixturePath, 'pnpm-lock.yaml'), 'utf8')
  const externalManifest = JSON.parse(readFileSync(resolve(fixturePath, 'package.json'), 'utf8'))
  mkdirSync(consumer)
  for (const name of ['package.json', 'pnpm-lock.yaml', 'pnpm-workspace.yaml']) {
    writeFileSync(resolve(consumer, name), readFileSync(resolve(fixturePath, name)))
  }
  const bytes = manifests.map(archive)
  const paths = bytes.map((data, index) => {
    const path = resolve(scratch, `${names[index]}.tgz`)
    writeFileSync(path, data)
    return path
  })
  const calls = []
  const failure = new Error('ordinary pnpm failure')
  const run = (command, args, options) => {
    calls.push({ command, args, cwd: options.cwd })
    if (calls.length === failAt) throw failure
    if (calls.length === 2) {
      writeFileSync(resolve(consumer, 'pnpm-lock.yaml'), mutateLock(derivedLock(bytes, externalBaseline)))
      const derived = { ...externalManifest, dependencies: { ...externalManifest.dependencies, ...Object.fromEntries(ids.map((id, index) => [id, specs[index]])) } }
      writeFileSync(resolve(consumer, 'package.json'), JSON.stringify(mutateManifest(derived)))
    }
    afterCall(calls.length, { consumer, paths, bytes })
    return ''
  }
  return { consumer, bytes, paths, run, calls, failure }
}

test('the external graph is the captured pnpm graph, not an expectation computed by the helper', () => {
  assert.equal(createHash('sha256').update(baseline).digest('hex'), '2a9aa0df9998309ba77aab5f9b9e7d7a759f2812d7d8e04158c2a08708fee51e')
  assert.deepEqual(baselineManifest.dependencies, {
    '@types/react': '19.2.18', react: '19.2.8', 'react-dom': '19.2.8', typescript: '5.9.3',
    '@radix-ui/react-slot': '1.3.3', clsx: '2.1.1', 'tailwind-merge': '3.6.0',
  })
})

test('the actual primary consumer call uses the real helper with four artifacts and three ordered pnpm commands', t => {
  const state = stage(t)
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const start = source.indexOf('  installFrozenUiConsumer(', source.indexOf('function verifyNpm()'))
  const end = source.indexOf('  const output = run(process.execPath', start)
  assert.ok(start > 0 && end > start, 'actual primary consumer install boundary')
  const call = source.slice(start, end)
  new Function('installFrozenUiConsumer', 'consumer', 'artifact', 'contractsArtifact', 'ruleEngineArtifact', 'ruleAuthoringArtifact', 'run', call)(
    installFrozenUiConsumer, state.consumer, ...state.paths, state.run)
  assert.deepEqual(state.calls, [
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--ignore-scripts'], cwd: state.consumer },
    { command: 'pnpm', args: ['add', '--lockfile-only', '--offline', '--ignore-scripts', 'file:.packed/ui-react.tgz', 'file:.packed/contracts.tgz', 'file:.packed/rule-engine.tgz', 'file:.packed/rule-authoring.tgz'], cwd: state.consumer },
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], cwd: state.consumer },
  ])
  for (const [index, name] of names.entries()) {
    assert.deepEqual(readFileSync(resolve(state.consumer, '.packed', `${name}.tgz`)), state.bytes[index])
    assert.deepEqual(readFileSync(state.paths[index]), state.bytes[index])
  }
  const body = source.slice(source.indexOf('function verifyNpm()'), source.indexOf('function verifyCopilotContractsNpm()'))
  assert.doesNotMatch(body, /['"]install['"]|package-lock=false/, 'primary consumer must not bypass the helper with an unlocked install')
  assert.match(source, /import \{[^}]*\binstallFrozenUiConsumer\b[^}]*\} from '\.\/package-fixture-frozen-install\.mjs'/)
})

test('a stale committed lock failure stops before staging or local derivation', t => {
  const state = stage(t, { failAt: 1 })
  assert.throws(() => installFrozenUiConsumer(state.consumer, state.paths, state.run), error => error === state.failure)
  assert.equal(state.calls.length, 1)
  assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
  assert.equal(readFileSync(resolve(state.consumer, 'pnpm-lock.yaml'), 'utf8'), baseline)
})

for (const failAt of [2, 3]) {
  test(`ordinary pnpm failure at command ${failAt} propagates without fallback`, t => {
    const state = stage(t, { failAt })
    assert.throws(() => installFrozenUiConsumer(state.consumer, state.paths, state.run), error => error === state.failure)
    assert.equal(state.calls.length, failAt)
  })
}

const invalidLocks = [
  ['changed external integrity', text => text.replace('sha512-+48PbAAb', 'sha512-CHANGED'), /external packages changed/],
  ['extra registry package', text => text.replace('packages:\n', 'packages:\n\n  extra@1.0.0: {}\n'), /unexpected packages entry/],
  ['extra importer dependency', text => text.replace('    dependencies:\n', '    dependencies:\n      extra:\n        specifier: 1.0.0\n        version: 1.0.0\n'), /unexpected importer dependency/],
  ['changed external importer peer context', text => text.replace('version: 19.2.8(react@19.2.8)', 'version: 19.2.8(react@18.0.0)'), /external importer changed/],
  ['changed external snapshot edge', text => text.replace('scheduler: 0.27.0', 'scheduler: 0.26.0'), /external snapshots changed/],
  ['duplicate section', text => `${text}\nsettings:\n  autoInstallPeers: false\n`, /duplicate lock key/],
  ['unsupported section', text => `${text}\noverrides: {}\n`, /unknown lock section/],
  ['malformed mapping', text => text.replace('settings:\n', 'settings: &settings\n'), /mixed scalar and mapping/],
  ['changed settings', text => text.replace('autoInstallPeers: false', 'autoInstallPeers: true'), /settings changed/],
  ['wrong local archive path', text => text.replace('tarball: file:.packed/ui-react.tgz', 'tarball: file:..\/ui-react.tgz'), /local tarball integrity\/path mismatch/],
  ['wrong local integrity', text => text.replace(/resolution: \{integrity: sha512-[^,]+, tarball: file:\.packed\/ui-react.tgz\}/, 'resolution: {integrity: sha512-wrong, tarball: file:.packed/ui-react.tgz}'), /local tarball integrity\/path mismatch/],
  ['wrong local package identity', text => text.replace("'@harborline-software/contracts@file:.packed/contracts.tgz':", "'@harborline-software/api-contracts@file:.packed/contracts.tgz':"), /unexpected packages entry/],
  ['wrong cross-package target', text => text.replace("'@harborline-software/contracts': file:.packed/contracts.tgz", "'@harborline-software/contracts': file:.packed/rule-engine.tgz"), /local dependency target changed/],
  ['duplicate importer', text => text.replace("      '@harborline-software/contracts':\n", "      '@harborline-software/contracts':\n        specifier: file:.packed/contracts.tgz\n        version: file:.packed/contracts.tgz\n      '@harborline-software/contracts':\n"), /duplicate lock key/],
  ['changed peer context', text => text.replaceAll('(react@19.2.8)', '(react@18.0.0)'), /external importer changed|external snapshots changed|local peer context target changed/],
  ['changed local peer context', text => text.replaceAll('(@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz))', '(@harborline-software/rule-engine@file:.packed/contracts.tgz)'), /local peer context target changed/],
  ['changed local optionality', text => text.replace("      '@harborline-software/contracts': file:.packed/contracts.tgz\n", '')
    .replace('      clsx: 2.1.1\n', "      clsx: 2.1.1\n      '@harborline-software/contracts': file:.packed/contracts.tgz\n"), /local dependency optionality changed/],
  ['extra local snapshot', text => text.replace('snapshots:\n', "snapshots:\n\n  '@harborline-software/extra@file:.packed/extra.tgz': {}\n"), /unexpected snapshots entry/],
  ['missing local optional peer', text => text.replace("      '@harborline-software/contracts': file:.packed/contracts.tgz\n", ''), /local dependency edges changed/],
  ['extra transitive peer', text => text.replace("      - '@types/react'", "      - '@types/react'\n      - typescript"), /transitive peer declarations changed/],
]

for (const [name, mutateLock, expected] of invalidLocks) {
  test(`${name} refuses before the final installation`, t => {
    const state = stage(t, { mutateLock })
    assert.throws(() => installFrozenUiConsumer(state.consumer, state.paths, state.run), expected)
    assert.equal(state.calls.length, 2)
  })
}

test('changing an external manifest dependency during offline derivation refuses', t => {
  const state = stage(t, { mutateManifest: value => ({ ...value, dependencies: { ...value.dependencies, react: '18.0.0' } }) })
  assert.throws(() => installFrozenUiConsumer(state.consumer, state.paths, state.run), /derived manifest changed external inputs/)
  assert.equal(state.calls.length, 2)
})

test('a baseline install that rewrites the committed lock refuses before local derivation', t => {
  const state = stage(t, { afterCall: (count, { consumer }) => {
    if (count === 1) writeFileSync(resolve(consumer, 'pnpm-lock.yaml'), `${baseline}\n`)
  } })
  assert.throws(() => installFrozenUiConsumer(state.consumer, state.paths, state.run), /baseline install rewrote committed lock/)
  assert.equal(state.calls.length, 1)
})

test('changed staged tarball bytes refuse before final installation', t => {
  const state = stage(t, { afterCall: (count, { consumer }) => {
    if (count === 2) writeFileSync(resolve(consumer, '.packed/ui-react.tgz'), 'different archive')
  } })
  assert.throws(() => installFrozenUiConsumer(state.consumer, state.paths, state.run), /staged tarball bytes changed/)
  assert.equal(state.calls.length, 2)
})

test('a different package under an expected tarball filename refuses before derivation', t => {
  const state = stage(t)
  writeFileSync(state.paths[0], archive({ name: '@harborline-software/extra', version: '0.8.0-alpha' }))
  assert.throws(() => installFrozenUiConsumer(state.consumer, state.paths, state.run), /packed artifact identity mismatch/)
  assert.equal(state.calls.length, 1)
})

test('a non-archive cannot supply a package identity or integrity', t => {
  const state = stage(t)
  writeFileSync(state.paths[0], 'not a tarball')
  assert.throws(() => installFrozenUiConsumer(state.consumer, state.paths, state.run))
  assert.equal(state.calls.length, 1)
})

test('a corrupted tar header refuses even when the gzip envelope is valid', t => {
  const state = stage(t)
  const tar = gunzipSync(state.bytes[0])
  tar[0] ^= 1
  writeFileSync(state.paths[0], gzipSync(tar))
  assert.throws(() => installFrozenUiConsumer(state.consumer, state.paths, state.run), /packed header checksum/)
  assert.equal(state.calls.length, 1)
})

test('final installation cannot silently rewrite the checked derived lock', t => {
  const state = stage(t, { afterCall: (count, { consumer }) => {
    if (count === 3) writeFileSync(resolve(consumer, 'pnpm-lock.yaml'), baseline)
  } })
  assert.throws(() => installFrozenUiConsumer(state.consumer, state.paths, state.run), /final install rewrote checked lock/)
  assert.equal(state.calls.length, 3)
})

// Exercise only the actual install boundary; importing the full driver would resolve the SDK.
function dynamicFormsCall() {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const body = source.slice(source.indexOf('function verifyDynamicFormsCapability()'), source.indexOf('function verifyWorkflowsCapability()'))
  const start = body.indexOf('  installFrozenUiConsumer(')
  const end = body.indexOf('  assertPackedInstall(', start)
  assert.ok(start > 0 && end > start, 'actual Dynamic Forms frozen install boundary')
  assert.doesNotMatch(body, /installPackedArtifacts\(|['"]install['"]|package-lock=false/, 'Dynamic Forms must not bypass frozen installation')
  assert.ok(end < body.indexOf('proofLines(clientRun('), 'frozen install precedes the unchanged exercise')
  return body.slice(start, end)
}

function runDynamicFormsInstall(state, paths = state.paths) {
  const prefixes = [
    'harborline-software-ui-react-', 'harborline-software-contracts-',
    'harborline-software-rule-engine-', 'harborline-software-rule-authoring-',
  ]
  const lookups = []
  const packedArtifact = (prefix, label) => {
    assert.equal(label, 'Dynamic Forms renderer')
    const index = prefixes.indexOf(prefix)
    assert.ok(index >= 0, 'known packed product prefix')
    lookups.push(prefix)
    return paths[index]
  }
  new Function('installFrozenUiConsumer', 'rendererConsumer', 'packedArtifact', 'run', dynamicFormsCall())(
    installFrozenUiConsumer, state.consumer, packedArtifact, state.run)
  assert.deepEqual(lookups, prefixes, 'actual caller artifact order')
}

test('the Dynamic Forms external graph is the actual pnpm capture with seven literal pins', () => {
  assert.equal(createHash('sha256').update(dynamicBaseline).digest('hex'), 'b3dcc2b3ce6910a8db8d31bbc421c8841ad9a79e325a7d7af0ecf520c59c839f')
  assert.equal(dynamicManifest.packageManager, 'pnpm@11.1.3')
  assert.deepEqual(dynamicManifest.dependencies, {
    jsdom: '26.1.0', react: '19.2.8', 'react-dom': '19.2.8',
    '@radix-ui/react-slot': '1.3.3', clsx: '2.1.1', 'tailwind-merge': '3.6.0', '@types/react': '19.2.18',
  })
})

test('the actual Dynamic Forms caller uses the unchanged helper and three ordered frozen operations', t => {
  const state = stage(t, { fixturePath: dynamicFixture })
  runDynamicFormsInstall(state)
  assert.deepEqual(state.calls, [
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--ignore-scripts'], cwd: state.consumer },
    { command: 'pnpm', args: ['add', '--lockfile-only', '--offline', '--ignore-scripts', 'file:.packed/ui-react.tgz', 'file:.packed/contracts.tgz', 'file:.packed/rule-engine.tgz', 'file:.packed/rule-authoring.tgz'], cwd: state.consumer },
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], cwd: state.consumer },
  ])
  for (const [index, name] of names.entries()) {
    assert.deepEqual(readFileSync(resolve(state.consumer, '.packed', `${name}.tgz`)), state.bytes[index])
    assert.deepEqual(readFileSync(state.paths[index]), state.bytes[index])
    const integrity = `sha512-${createHash('sha512').update(state.bytes[index]).digest('base64')}`
    assert.ok(readFileSync(resolve(state.consumer, 'pnpm-lock.yaml'), 'utf8').includes(`resolution: {integrity: ${integrity}, tarball: ${specs[index]}}`))
  }
})

for (const failAt of [1, 2, 3]) {
  test(`Dynamic Forms ordinary failure at operation ${failAt} stops without fallback`, t => {
    const state = stage(t, { fixturePath: dynamicFixture, failAt })
    assert.throws(() => runDynamicFormsInstall(state), error => error === state.failure)
    assert.equal(state.calls.length, failAt)
    if (failAt === 1) {
      assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
      assert.equal(readFileSync(resolve(state.consumer, 'pnpm-lock.yaml'), 'utf8'), dynamicBaseline)
    }
  })
}

const invalidDynamicLocks = [
  ['changed jsdom integrity', text => text.replace('sha512-Cvc9WUhx', 'sha512-CHANGED'), /external packages changed/],
  ['changed jsdom importer pin', text => text.replace('specifier: 26.1.0', 'specifier: 26.1.1'), /external importer changed/],
  ['changed jsdom snapshot edge', text => text.replace('cssstyle: 4.6.0', 'cssstyle: 4.5.0'), /external snapshots changed/],
  ['extra external package', text => text.replace('packages:\n', 'packages:\n\n  extra@1.0.0: {}\n'), /unexpected packages entry/],
  ['extra importer dependency', text => text.replace('    dependencies:\n', '    dependencies:\n      extra:\n        specifier: 1.0.0\n        version: 1.0.0\n'), /unexpected importer dependency/],
  ['changed local integrity', text => text.replace(/resolution: \{integrity: sha512-[^,]+, tarball: file:\.packed\/ui-react.tgz\}/, 'resolution: {integrity: sha512-wrong, tarball: file:.packed/ui-react.tgz}'), /local tarball integrity\/path mismatch/],
  ['changed local peer edge', text => text.replace("'@harborline-software/contracts': file:.packed/contracts.tgz", "'@harborline-software/contracts': file:.packed/rule-engine.tgz"), /local dependency target changed/],
]

for (const [name, mutateLock, expected] of invalidDynamicLocks) {
  test(`Dynamic Forms ${name} refuses before final installation`, t => {
    const state = stage(t, { fixturePath: dynamicFixture, mutateLock })
    assert.throws(() => runDynamicFormsInstall(state), expected)
    assert.equal(state.calls.length, 2)
  })
}

test('Dynamic Forms swapped packed artifacts refuse before local derivation', t => {
  const state = stage(t, { fixturePath: dynamicFixture })
  const wrongOrder = [state.paths[1], state.paths[0], ...state.paths.slice(2)]
  assert.throws(() => runDynamicFormsInstall(state, wrongOrder), /packed artifact identity mismatch/)
  assert.equal(state.calls.length, 1)
})

test('Dynamic Forms stale committed importer refuses before the first installation', t => {
  const state = stage(t, { fixturePath: dynamicFixture })
  writeFileSync(resolve(state.consumer, 'pnpm-lock.yaml'), dynamicBaseline.replace('specifier: 26.1.0', 'specifier: 26.1.1'))
  assert.throws(() => runDynamicFormsInstall(state), /committed dependency pin mismatch/)
  assert.equal(state.calls.length, 0)
  assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
})


import { installFrozenContractsConsumer } from '../package-fixture-frozen-install.mjs'

const contractsFixture = resolve(root, 'tests/package-consumers/forms-contracts-npm')
// Independent actual 031 pnpm output, not generated by the installer under test.
const contractsCapturedDerived = "lockfileVersion: '9.0'\n\nsettings:\n  autoInstallPeers: false\n  excludeLinksFromLockfile: false\n\nimporters:\n\n  .:\n    dependencies:\n      '@harborline-software/contracts':\n        specifier: file:.packed/contracts.tgz\n        version: file:.packed/contracts.tgz\n\npackages:\n\n  '@harborline-software/contracts@file:.packed/contracts.tgz':\n    resolution: {integrity: sha512-FqFCdc1ruwLomAAtMcG9Upc8nn6j9AFqKrj/Jj3DvUYie2qEQTVb5fdkV3iMdKlIWEbu3EkpLu2H+iMjwGvbew==, tarball: file:.packed/contracts.tgz}\n    version: 0.0.0-alpha.0\n\nsnapshots:\n\n  '@harborline-software/contracts@file:.packed/contracts.tgz': {}\n"

const contractsCapturedIntegrity = 'sha512-FqFCdc1ruwLomAAtMcG9Upc8nn6j9AFqKrj/Jj3DvUYie2qEQTVb5fdkV3iMdKlIWEbu3EkpLu2H+iMjwGvbew=='

function contractsState(t, { manifest = { name: '@harborline-software/contracts', version: '0.0.0-alpha.0' }, mutateLock = value => value, mutateManifest = value => value, failAt, afterCall = () => {} } = {}) {
  const scratch = mkdtempSync(resolve(tmpdir(), 'harborline-contracts-frozen-'))
  t.after(() => rmSync(scratch, { recursive: true, force: true }))
  const consumer = resolve(scratch, 'consumer')
  mkdirSync(consumer)
  for (const file of ['package.json', 'pnpm-lock.yaml', 'pnpm-workspace.yaml']) {
    writeFileSync(resolve(consumer, file), readFileSync(resolve(contractsFixture, file)))
  }
  const bytes = archive(manifest)
  const artifact = resolve(scratch, 'contracts.tgz')
  writeFileSync(artifact, bytes)
  const calls = []
  const failure = new Error('ordinary Contracts pnpm failure')
  const run = (command, args, options) => {
    calls.push({ command, args, cwd: options.cwd })
    if (calls.length === failAt) throw failure
    if (calls.length === 2) {
      const integrity = `sha512-${createHash('sha512').update(bytes).digest('base64')}`
      const derived = contractsCapturedDerived.replace(contractsCapturedIntegrity, integrity)
        .replace('    version: 0.0.0-alpha.0\n', `    version: ${manifest.version}\n`)
      writeFileSync(resolve(consumer, 'pnpm-lock.yaml'), mutateLock(derived))
      writeFileSync(resolve(consumer, 'package.json'), JSON.stringify(mutateManifest({
        private: true, type: 'module', packageManager: 'pnpm@11.1.3',
        dependencies: { '@harborline-software/contracts': 'file:.packed/contracts.tgz' },
      })))
    }
    afterCall(calls.length, { consumer, artifact, bytes })
    return ''
  }
  return { consumer, artifact, bytes, calls, failure, run, reachedExercise: false }
}

function runContractsCaller(state) {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const body = source.slice(source.indexOf('function verifyFormsNpm()'), source.indexOf('function verifyRuleRuntimeNpm()'))
  const start = body.indexOf('  installFrozenContractsConsumer(')
  const end = body.indexOf('  const output = run(process.execPath', start)
  assert.ok(start > 0 && end > start, 'actual standalone Contracts install boundary')
  assert.match(body, /cpSync\(resolve\(root, 'tests\/package-consumers\/forms-contracts-npm'\), consumer, \{ recursive: true \}\)/)
  assert.doesNotMatch(body, /package-lock=false|installPackedArtifacts\(|run\('npm', \['install'/, 'no unlocked Contracts fallback')
  assert.match(source, /import \{[^}]*\binstallFrozenContractsConsumer\b[^}]*\} from '\.\/package-fixture-frozen-install\.mjs'/)
  new Function('installFrozenContractsConsumer', 'consumer', 'artifact', 'run', body.slice(start, end))(
    installFrozenContractsConsumer, state.consumer, state.artifact, state.run)
  state.reachedExercise = true
}

test('Contracts fixtures pin the actual empty baseline and independent singleton capture', () => {
  assert.equal(createHash('sha256').update(readFileSync(resolve(contractsFixture, 'pnpm-lock.yaml'))).digest('hex'), '878a7b60746fb21ffe11029327f29ea50cfc840bfeb9e0b3cbb46a77f828b7e7')
  assert.equal(createHash('sha256').update(contractsCapturedDerived).digest('hex'), '68887198eff08b791216eb5e6e122e58307260fa8f39d34d03bbc8b264dff8b2')
  assert.deepEqual(JSON.parse(readFileSync(resolve(contractsFixture, 'package.json'), 'utf8')), {
    private: true, type: 'module', packageManager: 'pnpm@11.1.3',
  })
  assert.equal(readFileSync(resolve(contractsFixture, 'pnpm-workspace.yaml'), 'utf8'), 'packages:\n  - .\nnodeLinker: hoisted\nautoInstallPeers: false\n')
})

test('the actual standalone Contracts caller uses one archive and three ordered frozen operations', t => {
  const state = contractsState(t)
  runContractsCaller(state)
  assert.deepEqual(state.calls, [
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--ignore-scripts'], cwd: state.consumer },
    { command: 'pnpm', args: ['add', '--lockfile-only', '--offline', '--ignore-scripts', 'file:.packed/contracts.tgz'], cwd: state.consumer },
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], cwd: state.consumer },
  ])
  assert.deepEqual(readFileSync(resolve(state.consumer, '.packed/contracts.tgz')), state.bytes)
  assert.deepEqual(readFileSync(state.artifact), state.bytes)
  assert.equal(state.reachedExercise, true)
})

test('Contracts singleton binds the actual archive version rather than a fixed product pin', t => {
  const state = contractsState(t, { manifest: { name: '@harborline-software/contracts', version: '0.0.0-alpha.2' } })
  runContractsCaller(state)
  assert.match(readFileSync(resolve(state.consumer, 'pnpm-lock.yaml'), 'utf8'), /    version: 0\.0\.0-alpha\.2\n/)
  assert.equal(state.calls.length, 3)
})

for (const failAt of [1, 2, 3]) test(`Contracts command ${failAt} failure stops before the next phase or exercise`, t => {
  const state = contractsState(t, { failAt })
  assert.throws(() => runContractsCaller(state), error => error === state.failure)
  assert.equal(state.calls.length, failAt)
  assert.equal(state.reachedExercise, false)
  if (failAt === 1) assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
})

test('a stale Contracts baseline refuses before the first install or staging', t => {
  const state = contractsState(t)
  writeFileSync(resolve(state.consumer, 'pnpm-lock.yaml'), 'stale\n')
  assert.throws(() => runContractsCaller(state), /Contracts empty baseline changed/)
  assert.equal(state.calls.length, 0)
  assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
  assert.equal(state.reachedExercise, false)
})

for (const file of ['pnpm-lock.yaml', 'package.json', 'pnpm-workspace.yaml']) test(`Contracts baseline install cannot repair ${file}`, t => {
  const state = contractsState(t, { afterCall: (number, { consumer }) => {
    if (number === 1) writeFileSync(resolve(consumer, file), 'changed\n')
  } })
  assert.throws(() => runContractsCaller(state), /Contracts baseline install rewrote/)
  assert.equal(state.calls.length, 1)
  assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
  assert.equal(state.reachedExercise, false)
})

for (const [label, manifest, failure] of [
  ['identity', { name: '@harborline-software/other', version: '0.0.0' }, /identity mismatch/],
  ['version', { name: '@harborline-software/contracts', version: 'unsafe\nversion' }, /unsupported Contracts package version/],
  ...['dependencies', 'optionalDependencies', 'peerDependencies', 'peerDependenciesMeta'].map(field => [field,
    { name: '@harborline-software/contracts', version: '0.0.0-alpha.0', [field]: { other: '1.0.0' } }, new RegExp(`Contracts packed ${field} must be empty`)]),
]) test(`Contracts archive ${label} refuses before derivation`, t => {
  const state = contractsState(t, { manifest })
  assert.throws(() => runContractsCaller(state), failure)
  assert.equal(state.calls.length, 1)
  assert.equal(state.reachedExercise, false)
})

test('Contracts invalid archive bytes refuse before local derivation', t => {
  const state = contractsState(t)
  writeFileSync(state.artifact, 'not a gzip archive')
  assert.throws(() => runContractsCaller(state))
  assert.equal(state.calls.length, 1)
  assert.equal(state.reachedExercise, false)
})

for (const [label, mutateLock] of [
  ['integrity', value => value.replace('integrity: sha512-', 'integrity: wrong-')],
  ['version', value => value.replace('    version: 0.0.0-alpha.0\n', '    version: 9.9.9\n')],
  ['path', value => value.replaceAll('file:.packed/contracts.tgz', 'file:.packed/other.tgz')],
  ['settings', value => value.replace('autoInstallPeers: false', 'autoInstallPeers: true')],
  ['importer', value => value.replace('importers:\n', 'importers:\n\n  other: {}\n')],
  ['package', value => value.replace('packages:\n', 'packages:\n\n  other@1.0.0: {}\n')],
  ['snapshot', value => value + '\n  other@1.0.0: {}\n'],
  ['peer context', value => value.replace('        version: file:.packed/contracts.tgz', '        version: file:.packed/contracts.tgz(other@1.0.0)')],
]) test(`Contracts changed singleton ${label} refuses before final installation`, t => {
  const state = contractsState(t, { mutateLock })
  assert.throws(() => runContractsCaller(state), /Contracts singleton lock graph changed/)
  assert.equal(state.calls.length, 2)
  assert.equal(state.reachedExercise, false)
})

test('Contracts offline derivation cannot add another manifest dependency', t => {
  const state = contractsState(t, { mutateManifest: value => ({ ...value, dependencies: { ...value.dependencies, other: '1.0.0' } }) })
  assert.throws(() => runContractsCaller(state), /Contracts derived manifest changed/)
  assert.equal(state.calls.length, 2)
  assert.equal(state.reachedExercise, false)
})

for (const [label, change] of [
  ['workspace', ({ consumer }) => writeFileSync(resolve(consumer, 'pnpm-workspace.yaml'), 'changed\n')],
  ['producer archive', ({ artifact }) => writeFileSync(artifact, 'changed')],
  ['staged archive', ({ consumer }) => writeFileSync(resolve(consumer, '.packed/contracts.tgz'), 'changed')],
]) test(`Contracts changed ${label} refuses before final installation`, t => {
  const state = contractsState(t, { afterCall: (number, context) => { if (number === 2) change(context) } })
  assert.throws(() => runContractsCaller(state), /Contracts (?:workspace|producer tarball|staged tarball) changed/)
  assert.equal(state.calls.length, 2)
  assert.equal(state.reachedExercise, false)
})

for (const file of ['pnpm-lock.yaml', 'package.json', 'pnpm-workspace.yaml', '.packed/contracts.tgz']) test(`Contracts final installation cannot rewrite ${file}`, t => {
  const state = contractsState(t, { afterCall: (number, { consumer }) => {
    if (number === 3) writeFileSync(resolve(consumer, file), 'changed\n')
  } })
  assert.throws(() => runContractsCaller(state), /Contracts (?:final install rewrote|workspace changed|staged tarball changed)/)
  assert.equal(state.calls.length, 3)
  assert.equal(state.reachedExercise, false)
})


import {cpSync, copyFileSync} from 'node:fs'

function runWorkflowsRendererInstall(state, {failCopy} = {}) {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const body = source.slice(source.indexOf('function verifyWorkflowsCapability()'), source.indexOf('function verifyCalculationsCapability()'))
  const start = body.indexOf('  cpSync(')
  const end = body.indexOf('  assertPackedInstall(', start)
  assert.ok(start > 0 && end > start, 'actual Workflows copy/install boundary')
  assert.doesNotMatch(body, /installPackedArtifacts\(|package-lock=false|run\('npm', \['install'/, 'no unlocked Workflows fallback')
  state.workflowCopies = []
  state.workflowLookups = []
  state.copyFailure = new Error('ordinary Workflows fixture copy failure')
  const copy = (kind, from, to, options) => {
    state.workflowCopies.push({kind, from, to})
    if (failCopy === kind) throw state.copyFailure
    if (kind === 'baseline') cpSync(from, to, options)
    else copyFileSync(from, to)
  }
  const packedArtifact = (prefix, label) => {
    state.workflowLookups.push({prefix, label})
    return state.artifact
  }
  new Function('cpSync', 'copyFileSync', 'resolve', 'root', 'rendererConsumer', 'corpusSource', 'installFrozenContractsConsumer', 'packedArtifact', 'run', body.slice(start, end))(
    (from, to, options) => copy('baseline', from, to, options),
    (from, to) => copy(to === resolve(state.consumer, 'exercise.mjs') ? 'exercise' : 'corpus', from, to),
    resolve, root, state.consumer, resolve(root, 'conformance/hlp.blocks.workflow/admission-mirror-cases.json'),
    installFrozenContractsConsumer, packedArtifact, state.run)
  // Control flow only: this unit test does not run the product admission exercise.
  state.reachedExercise = true
}

test('the actual Workflows renderer copies the admitted baseline and unchanged exercise/corpus before one frozen artifact', t => {
  const state = contractsState(t)
  rmSync(state.consumer, {recursive: true})
  const ordinaryRun = state.run
  state.run = (...args) => {
    if (state.calls.length === 0) {
      for (const file of ['package.json', 'pnpm-lock.yaml', 'pnpm-workspace.yaml']) {
        assert.deepEqual(readFileSync(resolve(state.consumer, file)), readFileSync(resolve(contractsFixture, file)))
      }
      assert.equal(createHash('sha256').update(readFileSync(resolve(state.consumer, 'exercise.mjs'))).digest('hex'), '4cac9e17c5259f2b6e1422ce28394a8d209275aaaa9f5320d1f189e875fca6ec')
      assert.equal(createHash('sha256').update(readFileSync(resolve(state.consumer, 'admission-mirror-cases.json'))).digest('hex'), 'bebe7d8774fb01a8cbc75cc37ebf37be905416d08c3ba2fa8b971970c2da2048')
    }
    return ordinaryRun(...args)
  }
  runWorkflowsRendererInstall(state)
  assert.deepEqual(state.workflowCopies, [
    {kind: 'baseline', from: resolve(root, 'tests/package-consumers/forms-contracts-npm'), to: state.consumer},
    {kind: 'exercise', from: resolve(root, 'tests/package-consumers/workflow-npm/exercise.mjs'), to: resolve(state.consumer, 'exercise.mjs')},
    {kind: 'corpus', from: resolve(root, 'conformance/hlp.blocks.workflow/admission-mirror-cases.json'), to: resolve(state.consumer, 'admission-mirror-cases.json')},
  ])
  assert.deepEqual(state.workflowLookups, [{prefix: 'harborline-software-contracts-', label: 'Workflows renderer'}])
  assert.deepEqual(state.calls, [
    {command: 'pnpm', args: ['install', '--frozen-lockfile', '--ignore-scripts'], cwd: state.consumer},
    {command: 'pnpm', args: ['add', '--lockfile-only', '--offline', '--ignore-scripts', 'file:.packed/contracts.tgz'], cwd: state.consumer},
    {command: 'pnpm', args: ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], cwd: state.consumer},
  ])
  assert.deepEqual(readFileSync(resolve(state.consumer, '.packed/contracts.tgz')), state.bytes)
  assert.deepEqual(readFileSync(state.artifact), state.bytes)
  assert.equal(state.reachedExercise, true)
})

for (const failAt of [1, 2, 3]) test(`Workflows frozen command ${failAt} failure stops before later work or exercise`, t => {
  const state = contractsState(t, {failAt})
  assert.throws(() => runWorkflowsRendererInstall(state), error => error === state.failure)
  assert.equal(state.calls.length, failAt)
  assert.equal(state.reachedExercise, false)
  assert.deepEqual(state.workflowLookups, [{prefix: 'harborline-software-contracts-', label: 'Workflows renderer'}])
})

for (const failCopy of ['baseline', 'exercise', 'corpus']) test(`Workflows ${failCopy} copy failure stops before artifact lookup or installation`, t => {
  const state = contractsState(t)
  assert.throws(() => runWorkflowsRendererInstall(state, {failCopy}), error => error === state.copyFailure)
  assert.equal(state.calls.length, 0)
  assert.deepEqual(state.workflowLookups, [])
  assert.equal(state.reachedExercise, false)
})

import { installFrozenRuleRuntimeConsumer } from '../package-fixture-frozen-install.mjs'

// pnpm 11.1.3 capture with the required Contracts peer; only archive integrity and explicit versions vary.
const ruleRuntimeCapturedLock = "lockfileVersion: '9.0'\n\nsettings:\n  autoInstallPeers: false\n  excludeLinksFromLockfile: false\n\nimporters:\n\n  .:\n    dependencies:\n      '@harborline-software/contracts':\n        specifier: file:.packed\\contracts.tgz\n        version: file:.packed/contracts.tgz\n      '@harborline-software/rule-engine':\n        specifier: file:.packed\\rule-engine.tgz\n        version: file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)\n\npackages:\n\n  '@harborline-software/contracts@file:.packed/contracts.tgz':\n    resolution: {integrity: sha512-d3ZFKmEI5/+lpfNjrln+5JMmdYk55OqsLUP0laj1Kf7dhXvVaE3+6cIMdYqKiC26ECU0q9mR94Dm0H7VXQ1Mbw==, tarball: file:.packed/contracts.tgz}\n    version: 0.0.0-alpha.0\n\n  '@harborline-software/rule-engine@file:.packed/rule-engine.tgz':\n    resolution: {integrity: sha512-JmBTJqoCJ6MpiJjagBn4psWMoDr6ORv0dyaA+6S0UH3PsMxMzJaYs8oggZHpcsJUIrVYrJGCKl5i0jNCqbWWbA==, tarball: file:.packed/rule-engine.tgz}\n    version: 0.1.0-alpha.0\n    peerDependencies:\n      '@harborline-software/contracts': 0.0.0-alpha.0\n\nsnapshots:\n\n  '@harborline-software/contracts@file:.packed/contracts.tgz': {}\n\n  '@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)':\n    dependencies:\n      '@harborline-software/contracts': file:.packed/contracts.tgz\n"
const ruleRuntimeCapturedManifest = "{\n  \"private\": true,\n  \"type\": \"module\",\n  \"packageManager\": \"pnpm@11.1.3\",\n  \"dependencies\": {\n    \"@harborline-software/contracts\": \u0022file:.packed\\\\contracts.tgz\",\n    \"@harborline-software/rule-engine\": \u0022file:.packed\\\\rule-engine.tgz\"\n  }\n}\n"
const ruleRuntimeCapturedSris = ["sha512-JmBTJqoCJ6MpiJjagBn4psWMoDr6ORv0dyaA+6S0UH3PsMxMzJaYs8oggZHpcsJUIrVYrJGCKl5i0jNCqbWWbA==", "sha512-d3ZFKmEI5/+lpfNjrln+5JMmdYk55OqsLUP0laj1Kf7dhXvVaE3+6cIMdYqKiC26ECU0q9mR94Dm0H7VXQ1Mbw=="]

function ruleRuntimeState(t, { manifest = structuredClone(manifests[2]), contractsManifest = structuredClone(manifests[1]), mutateLock = value => value, mutateManifest = value => value, failAt, afterCall = () => {}, afterCopy = () => {} } = {}) {
  const scratch = mkdtempSync(resolve(tmpdir(), 'harborline-rule-runtime-frozen-'))
  t.after(() => rmSync(scratch, { recursive: true, force: true }))
  const consumer = resolve(scratch, 'rule-runtime-npm-consumer')
  const bytes = archive(manifest)
  const artifact = resolve(scratch, 'selected-rule-engine-package.tgz')
  const contractsBytes = archive(contractsManifest)
  const contractsArtifact = resolve(scratch, 'selected-contracts-package.tgz')
  writeFileSync(artifact, bytes)
  writeFileSync(contractsArtifact, contractsBytes)
  const calls = []
  const failure = new Error('ordinary Rule Runtime pnpm failure')
  const run = (command, args, options) => {
    calls.push({ command, args, cwd: options.cwd })
    if (calls.length === failAt) throw failure
    if (calls.length === 2) {
      const integrity = `sha512-${createHash('sha512').update(bytes).digest('base64')}`
      const derived = ruleRuntimeCapturedLock.replace(ruleRuntimeCapturedSris[0], integrity)
        .replace(ruleRuntimeCapturedSris[1], `sha512-${createHash('sha512').update(contractsBytes).digest('base64')}`)
        .replace('    version: 0.1.0-alpha.0\n', `    version: ${manifest.version}\n`)
      writeFileSync(resolve(consumer, 'pnpm-lock.yaml'), mutateLock(derived))
      writeFileSync(resolve(consumer, 'package.json'), JSON.stringify(mutateManifest(JSON.parse(ruleRuntimeCapturedManifest))))
    }
    afterCall(calls.length, { consumer, artifact, bytes, contractsArtifact, contractsBytes })
    return ''
  }
  return { scratch, consumer, artifact, bytes, contractsArtifact, contractsBytes, calls, failure, run, afterCopy, helperCalls: [], copies: [], reachedNodeInvocation: false }
}

function runRuleRuntimeCaller(state, { failCopy = false } = {}) {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const body = source.slice(source.indexOf('function verifyRuleRuntimeNpm()'), source.indexOf('function verifyRuleAuthoringNpm()'))
  const start = body.indexOf('  const consumer = ')
  const install = body.indexOf('  installFrozenRuleRuntimeConsumer(')
  const end = body.indexOf('  const output = run(process.execPath', install)
  assert.ok(start > 0 && install > start && end > install, 'actual Rule Runtime copy/exercise-write/install boundary')
  assert.match(source, /import \{[^}]*\binstallFrozenRuleRuntimeConsumer\b[^}]*\} from '\.\/package-fixture-frozen-install\.mjs'/)
  assert.doesNotMatch(body, /package-lock=false|installPackedArtifacts\(|run\('npm', \['install'/, 'no unlocked Rule Runtime fallback')
  assert.ok(body.slice(end).startsWith("  const output = run(process.execPath, ['exercise.mjs'], { cwd: consumer }).trim()"), 'unchanged Node invocation follows successful installation')
  const exerciseStart = body.indexOf("  writeFileSync(resolve(consumer, 'exercise.mjs'),")
  assert.equal(createHash('sha256').update(body.slice(exerciseStart, install)).digest('hex'), '71fec1f0a9701665c385aadfc0b983f1f16ad9f095d009a4dd1b00c71b96da21', 'unchanged Options/clock/Render exercise source literal')
  state.copyFailure = new Error('ordinary Rule Runtime baseline copy failure')
  const copy = (from, to, options) => {
    state.copies.push({ from, to, options })
    if (failCopy) throw state.copyFailure
    cpSync(from, to, options)
    state.afterCopy(state)
  }
  const installSelected = (consumer, artifact, contracts, run) => {
    state.helperCalls.push({ consumer, artifact, contracts })
    installFrozenRuleRuntimeConsumer(consumer, artifact, contracts, run)
  }
  new Function('resolve', 'fixtureRoot', 'cpSync', 'root', 'writeFileSync', 'installFrozenRuleRuntimeConsumer', 'artifact', 'packedArtifact', 'run', body.slice(start, end))(
    resolve, state.scratch, copy, root, writeFileSync, installSelected, state.artifact, (prefix, label) => {
      assert.equal(prefix, 'harborline-software-contracts-')
      assert.equal(label, 'Rule Runtime consumer')
      return state.contractsArtifact
    }, state.run)
  // Control-flow boundary only; this test never invokes the product exercise or a real command.
  state.reachedNodeInvocation = true
}

test('Rule Runtime pair expectations bind actual pnpm output and the unchanged neutral baseline', () => {
  assert.equal(createHash('sha256').update(ruleRuntimeCapturedLock).digest('hex'), 'ee5dcb2efffd5ddcfd5d0324bddb384b6a7347db0ee062d5f75f7a9e7e3e516c')
  assert.equal(Buffer.byteLength(ruleRuntimeCapturedLock), 1345)
  assert.equal(createHash('sha256').update(ruleRuntimeCapturedManifest).digest('hex'), '5b3998525fc2e70f5072ef0a2630fd7aec89ac4163a0be7caf6b505f5044a67f')
  assert.equal(createHash('sha256').update(readFileSync(resolve(contractsFixture, 'package.json'))).digest('hex'), '1485792c7933b0c95a7f8a194307c86bc14676bd583a1e65b6a66a9c2f1dc2d1')
  assert.equal(createHash('sha256').update(readFileSync(resolve(contractsFixture, 'pnpm-lock.yaml'))).digest('hex'), '878a7b60746fb21ffe11029327f29ea50cfc840bfeb9e0b3cbb46a77f828b7e7')
  assert.equal(createHash('sha256').update(readFileSync(resolve(contractsFixture, 'pnpm-workspace.yaml'))).digest('hex'), 'c8b2f4c55a47976cdc45d898428f1127aafd3ba753d69fa3f18cde4c2ff59636')
})

test('the actual Rule Runtime caller copies the baseline and installs the selected archive with three ordered frozen operations', t => {
  const state = ruleRuntimeState(t)
  runRuleRuntimeCaller(state)
  assert.deepEqual(state.copies, [{ from: resolve(root, 'tests/package-consumers/forms-contracts-npm'), to: state.consumer, options: { recursive: true } }])
  assert.deepEqual(state.helperCalls, [{ consumer: state.consumer, artifact: state.artifact, contracts: state.contractsArtifact }])
  assert.deepEqual(state.calls, [
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--ignore-scripts'], cwd: state.consumer },
    { command: 'pnpm', args: ['add', '--lockfile-only', '--offline', '--ignore-scripts', 'file:.packed/rule-engine.tgz', 'file:.packed/contracts.tgz'], cwd: state.consumer },
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], cwd: state.consumer },
  ])
  assert.deepEqual(readFileSync(resolve(state.consumer, '.packed/rule-engine.tgz')), state.bytes)
  assert.deepEqual(readFileSync(state.artifact), state.bytes)
  assert.equal(readFileSync(resolve(state.consumer, 'pnpm-workspace.yaml'), 'utf8'), 'packages:\n  - .\nnodeLinker: hoisted\nautoInstallPeers: false\n')
  assert.equal(state.reachedNodeInvocation, true)
})

test('Rule Runtime pair derives the archive version while preserving the required Contracts peer', t => {
  const state = ruleRuntimeState(t, { manifest: { ...manifests[2], version: '0.1.0-alpha.2' } })
  runRuleRuntimeCaller(state)
  assert.match(readFileSync(resolve(state.consumer, 'pnpm-lock.yaml'), 'utf8'), /    version: 0\.1\.0-alpha\.2\n/)
  assert.equal(state.calls.length, 3)
})

test('Rule Runtime baseline copy failure propagates before writing the exercise or invoking the helper', t => {
  const state = ruleRuntimeState(t)
  assert.throws(() => runRuleRuntimeCaller(state, { failCopy: true }), error => error === state.copyFailure)
  assert.equal(state.calls.length, 0)
  assert.deepEqual(state.helperCalls, [])
  assert.equal(existsSync(resolve(state.consumer, 'exercise.mjs')), false)
  assert.equal(state.reachedNodeInvocation, false)
})

for (const failAt of [1, 2, 3]) test(`Rule Runtime command ${failAt} failure stops before later operations or exercise invocation`, t => {
  const state = ruleRuntimeState(t, { failAt })
  assert.throws(() => runRuleRuntimeCaller(state), error => error === state.failure)
  assert.equal(state.calls.length, failAt)
  assert.equal(state.reachedNodeInvocation, false)
  if (failAt === 1) assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
})

for (const [file, contents, expected] of [
  ['package.json', '{"private":true,"type":"module","packageManager":"pnpm@11.1.4"}', /Rule Runtime consumer manifest changed/],
  ['pnpm-lock.yaml', 'stale\n', /Rule Runtime empty baseline changed/],
  ['pnpm-workspace.yaml', 'packages: []\n', /Rule Runtime consumer workspace changed/],
]) test(`Rule Runtime unsupported baseline ${file} refuses before any installation or staging`, t => {
  const state = ruleRuntimeState(t, { afterCopy: ({ consumer }) => writeFileSync(resolve(consumer, file), contents) })
  assert.throws(() => runRuleRuntimeCaller(state), expected)
  assert.equal(state.calls.length, 0)
  assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
  assert.equal(state.reachedNodeInvocation, false)
})

for (const file of ['pnpm-lock.yaml', 'package.json', 'pnpm-workspace.yaml']) test(`Rule Runtime baseline installation cannot repair ${file}`, t => {
  const state = ruleRuntimeState(t, { afterCall: (number, { consumer }) => {
    if (number === 1) writeFileSync(resolve(consumer, file), 'changed\n')
  } })
  assert.throws(() => runRuleRuntimeCaller(state), /Rule Runtime baseline install rewrote/)
  assert.equal(state.calls.length, 1)
  assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
  assert.equal(state.reachedNodeInvocation, false)
})

for (const [label, manifest, expected] of [
  ['identity', { name: '@harborline-software/contracts', version: '0.1.0-alpha.0' }, /identity mismatch/],
  ['version', { name: '@harborline-software/rule-engine', version: 'unsafe\nversion' }, /unsupported Rule Runtime package version/],
  ...['dependencies', 'optionalDependencies'].map(field => [field,
    { ...manifests[2], [field]: { other: '1.0.0' } }, new RegExp(`Rule Runtime packed ${field} must be empty`)]),
]) test(`Rule Runtime archive ${label} refuses before local derivation`, t => {
  const state = ruleRuntimeState(t, { manifest })
  assert.throws(() => runRuleRuntimeCaller(state), expected)
  assert.equal(state.calls.length, 1)
  assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
  assert.equal(state.reachedNodeInvocation, false)
})

test('Rule Runtime malformed gzip refuses before local derivation', t => {
  const state = ruleRuntimeState(t)
  writeFileSync(state.artifact, 'not a gzip archive')
  assert.throws(() => runRuleRuntimeCaller(state))
  assert.equal(state.calls.length, 1)
  assert.equal(state.reachedNodeInvocation, false)
})

test('Rule Runtime corrupt packed header refuses despite a valid gzip envelope', t => {
  const state = ruleRuntimeState(t)
  const tar = gunzipSync(state.bytes)
  tar[0] ^= 1
  writeFileSync(state.artifact, gzipSync(tar))
  assert.throws(() => runRuleRuntimeCaller(state), /packed header checksum/)
  assert.equal(state.calls.length, 1)
  assert.equal(state.reachedNodeInvocation, false)
})

for (const [label, mutateLock, expected] of [
  ['integrity', value => value.replace('integrity: sha512-', 'integrity: wrong-'), /local tarball integrity\/path mismatch/],
  ['version', value => value.replace('    version: 0.1.0-alpha.0\n', '    version: 9.9.9\n'), /local package version mismatch/],
  ['path', value => value.replaceAll('file:.packed/rule-engine.tgz', 'file:.packed/other.tgz'), /local reference changed its tarball path/],
  ['settings', value => value.replace('autoInstallPeers: false', 'autoInstallPeers: true'), /settings changed/],
  ['importer', value => value.replace('importers:\n', 'importers:\n\n  other: {}\n'), /only the consumer importer/],
  ['package', value => value.replace('packages:\n', 'packages:\n\n  other@1.0.0: {}\n'), /unexpected packages entry/],
  ['snapshot', value => value + '\n  other@1.0.0: {}\n', /unexpected snapshots entry/],
  ['peer context', value => value.replace('        version: file:.packed/rule-engine.tgz', '        version: file:.packed/rule-engine.tgz(other@1.0.0)'), /unexpected snapshots entry/],
  ['dependency edge', value => value.replace("      '@harborline-software/contracts': file:.packed/contracts.tgz\n", "      other: file:.packed/contracts.tgz\n"), /local dependency edges changed/],
]) test(`Rule Runtime changed pair ${label} refuses before final installation`, t => {
  const state = ruleRuntimeState(t, { mutateLock })
  assert.throws(() => runRuleRuntimeCaller(state), expected)
  assert.equal(state.calls.length, 2)
  assert.equal(state.reachedNodeInvocation, false)
})

test('Rule Runtime offline derivation cannot add an unadmitted manifest dependency', t => {
  const state = ruleRuntimeState(t, { mutateManifest: value => ({ ...value, dependencies: { ...value.dependencies, other: '1.0.0' } }) })
  assert.throws(() => runRuleRuntimeCaller(state), /Rule Runtime derived manifest changed/)
  assert.equal(state.calls.length, 2)
  assert.equal(state.reachedNodeInvocation, false)
})

for (const [label, change] of [
  ['workspace', ({ consumer }) => writeFileSync(resolve(consumer, 'pnpm-workspace.yaml'), 'changed\n')],
  ['producer archive', ({ artifact }) => writeFileSync(artifact, 'changed')],
  ['staged archive', ({ consumer }) => writeFileSync(resolve(consumer, '.packed/rule-engine.tgz'), 'changed')],
]) test(`Rule Runtime changed ${label} refuses before final installation`, t => {
  const state = ruleRuntimeState(t, { afterCall: (number, context) => { if (number === 2) change(context) } })
  assert.throws(() => runRuleRuntimeCaller(state), /Rule Runtime (?:workspace|producer tarball|staged tarball) changed/)
  assert.equal(state.calls.length, 2)
  assert.equal(state.reachedNodeInvocation, false)
})

for (const [file, target, expected] of [
  ['pnpm-lock.yaml', 'consumer', /Rule Runtime final install rewrote lock/],
  ['package.json', 'consumer', /Rule Runtime final install rewrote manifest/],
  ['pnpm-workspace.yaml', 'consumer', /Rule Runtime workspace changed/],
  ['.packed/rule-engine.tgz', 'consumer', /Rule Runtime staged tarball changed/],
  ['producer archive', 'producer', /Rule Runtime producer tarball changed/],
]) test(`Rule Runtime final installation cannot rewrite ${file}`, t => {
  const state = ruleRuntimeState(t, { afterCall: (number, { consumer, artifact }) => {
    if (number === 3) writeFileSync(target === 'producer' ? artifact : resolve(consumer, file), 'changed\n')
  } })
  assert.throws(() => runRuleRuntimeCaller(state), expected)
  assert.equal(state.calls.length, 3)
  assert.equal(state.reachedNodeInvocation, false)
})

import { installFrozenRuleAuthoringConsumer } from '../package-fixture-frozen-install.mjs'

// pnpm 11.1.3 triple capture preserves the selected runtime and its required Contracts peer.
const ruleAuthoringCapturedLock = "lockfileVersion: '9.0'\n\nsettings:\n  autoInstallPeers: false\n  excludeLinksFromLockfile: false\n\nimporters:\n\n  .:\n    dependencies:\n      '@harborline-software/contracts':\n        specifier: file:.packed\\contracts.tgz\n        version: file:.packed/contracts.tgz\n      '@harborline-software/rule-authoring':\n        specifier: file:.packed\\rule-authoring.tgz\n        version: file:.packed/rule-authoring.tgz(@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz))\n      '@harborline-software/rule-engine':\n        specifier: file:.packed\\rule-engine.tgz\n        version: file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)\n\npackages:\n\n  '@harborline-software/contracts@file:.packed/contracts.tgz':\n    resolution: {integrity: sha512-d3ZFKmEI5/+lpfNjrln+5JMmdYk55OqsLUP0laj1Kf7dhXvVaE3+6cIMdYqKiC26ECU0q9mR94Dm0H7VXQ1Mbw==, tarball: file:.packed/contracts.tgz}\n    version: 0.0.0-alpha.0\n\n  '@harborline-software/rule-authoring@file:.packed/rule-authoring.tgz':\n    resolution: {integrity: sha512-DFxoLjFfqf7V5OBCtx8O1mhWTe14c9cY9SNQpUawqNoCl1IBrL/xkn0cFHtCZhJP8JdlPAcnBy/G5DUoJM/trA==, tarball: file:.packed/rule-authoring.tgz}\n    version: 0.1.0-alpha.0\n    peerDependencies:\n      '@harborline-software/rule-engine': 0.1.0-alpha.0\n    peerDependenciesMeta:\n      '@harborline-software/rule-engine':\n        optional: true\n\n  '@harborline-software/rule-engine@file:.packed/rule-engine.tgz':\n    resolution: {integrity: sha512-JmBTJqoCJ6MpiJjagBn4psWMoDr6ORv0dyaA+6S0UH3PsMxMzJaYs8oggZHpcsJUIrVYrJGCKl5i0jNCqbWWbA==, tarball: file:.packed/rule-engine.tgz}\n    version: 0.1.0-alpha.0\n    peerDependencies:\n      '@harborline-software/contracts': 0.0.0-alpha.0\n\nsnapshots:\n\n  '@harborline-software/contracts@file:.packed/contracts.tgz': {}\n\n  '@harborline-software/rule-authoring@file:.packed/rule-authoring.tgz(@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz))':\n    optionalDependencies:\n      '@harborline-software/rule-engine': file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)\n\n  '@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)':\n    dependencies:\n      '@harborline-software/contracts': file:.packed/contracts.tgz\n"
const ruleAuthoringCapturedManifest = "{\n  \"private\": true,\n  \"type\": \"module\",\n  \"packageManager\": \"pnpm@11.1.3\",\n  \"dependencies\": {\n    \"@harborline-software/contracts\": \u0022file:.packed\\\\contracts.tgz\",\n    \"@harborline-software/rule-authoring\": \u0022file:.packed\\\\rule-authoring.tgz\",\n    \"@harborline-software/rule-engine\": \u0022file:.packed\\\\rule-engine.tgz\"\n  }\n}\n"
const ruleAuthoringCapturedSris = ["sha512-DFxoLjFfqf7V5OBCtx8O1mhWTe14c9cY9SNQpUawqNoCl1IBrL/xkn0cFHtCZhJP8JdlPAcnBy/G5DUoJM/trA==", "sha512-JmBTJqoCJ6MpiJjagBn4psWMoDr6ORv0dyaA+6S0UH3PsMxMzJaYs8oggZHpcsJUIrVYrJGCKl5i0jNCqbWWbA==", "sha512-d3ZFKmEI5/+lpfNjrln+5JMmdYk55OqsLUP0laj1Kf7dhXvVaE3+6cIMdYqKiC26ECU0q9mR94Dm0H7VXQ1Mbw=="]
const ruleAuthoringTestManifests = [
  { name: '@harborline-software/rule-authoring', version: '0.1.0-alpha.0',
    peerDependencies: { '@harborline-software/rule-engine': '0.1.0-alpha.0' },
    peerDependenciesMeta: { '@harborline-software/rule-engine': { optional: true } } },
  structuredClone(manifests[2]),
  structuredClone(manifests[1]),
]

function ruleAuthoringState(t, { packedManifests = structuredClone(ruleAuthoringTestManifests), mutateLock = value => value, mutateManifest = value => value, failAt, afterCall = () => {}, afterCopy = () => {} } = {}) {
  const scratch = mkdtempSync(resolve(tmpdir(), 'harborline-rule-authoring-frozen-'))
  t.after(() => rmSync(scratch, { recursive: true, force: true }))
  const consumer = resolve(scratch, 'rule-authoring-npm-consumer')
  const npmArtifacts = resolve(scratch, 'npm')
  mkdirSync(npmArtifacts)
  const bytes = packedManifests.map(archive)
  const engineFilename = 'harborline-software-rule-engine-selected.tgz'
  const paths = [resolve(npmArtifacts, 'selected-authoring.tgz'), resolve(npmArtifacts, engineFilename), resolve(npmArtifacts, 'harborline-software-contracts-selected.tgz')]
  for (const [index, path] of paths.entries()) writeFileSync(path, bytes[index])
  const calls = []
  const failure = new Error('ordinary Rule Authoring pnpm failure')
  const run = (command, args, options) => {
    calls.push({ command, args, cwd: options.cwd })
    if (calls.length === failAt) throw failure
    if (calls.length === 2) {
      const sris = bytes.map(value => `sha512-${createHash('sha512').update(value).digest('base64')}`)
      const derived = ruleAuthoringCapturedSris.reduce((text, sri, index) => text.replace(sri, sris[index]), ruleAuthoringCapturedLock)
        .replace('    version: 0.1.0-alpha.0\n    peerDependencies:', `    version: ${packedManifests[0].version}\n    peerDependencies:`)
        .replace("      '@harborline-software/rule-engine': 0.1.0-alpha.0\n", `      '@harborline-software/rule-engine': ${packedManifests[1].version}\n`)
        .replace('tarball: file:.packed/rule-engine.tgz}\n    version: 0.1.0-alpha.0\n', `tarball: file:.packed/rule-engine.tgz}\n    version: ${packedManifests[1].version}\n`)
      writeFileSync(resolve(consumer, 'pnpm-lock.yaml'), mutateLock(derived))
      writeFileSync(resolve(consumer, 'package.json'), JSON.stringify(mutateManifest(JSON.parse(ruleAuthoringCapturedManifest))))
    }
    afterCall(calls.length, { consumer, paths, bytes })
    return ''
  }
  return { scratch, consumer, npmArtifacts, paths, bytes, engineFilename, calls, failure, run, afterCopy, copies: [], helperCalls: [], lookups: [], reachedNodeInvocation: false }
}

function runRuleAuthoringCaller(state, { failCopy = false, engineNames = [state.engineFilename, 'unrelated.tgz', 'harborline-software-rule-engine-ignored.txt'] } = {}) {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const body = source.slice(source.indexOf('function verifyRuleAuthoringNpm()'), source.indexOf('function verifyNuget()'))
  const start = body.indexOf('  const consumer = ')
  const install = body.indexOf('  installFrozenRuleAuthoringConsumer(')
  const end = body.indexOf('  const output = run(process.execPath', install)
  assert.ok(start > 0 && install > start && end > install, 'actual Rule Authoring scratch/copy/engine lookup/install boundary')
  assert.match(source, /import \{[^}]*\binstallFrozenRuleAuthoringConsumer\b[^}]*\} from '\.\/package-fixture-frozen-install\.mjs'/)
  assert.doesNotMatch(body, /package-lock=false|installPackedArtifacts\(|run\('npm', \['install'/, 'no unlocked Rule Authoring fallback')
  assert.ok(body.slice(end).startsWith("  const output = run(process.execPath, ['exercise.mjs'], { cwd: consumer }).trim()"), 'unchanged Node invocation after successful install')
  assert.equal(createHash('sha256').update(readFileSync(resolve(root, 'tests/package-consumers/rule-authoring-npm/exercise.mjs'))).digest('hex'), '403da6fa301c09ae3323ef5de878d691ffe04cd0c387a519b5d9a70959593a8f')
  state.copyFailure = new Error('ordinary Rule Authoring baseline copy failure')
  const copy = (from, to, options) => {
    state.copies.push({ from, to, options })
    if (failCopy) throw state.copyFailure
    cpSync(from, to, options)
    state.afterCopy(state)
  }
  const select = directory => { state.lookups.push(directory); return [...engineNames, 'harborline-software-contracts-selected.tgz'] }
  const installSelected = (consumer, authoring, engine, contracts, run) => {
    state.helperCalls.push({ consumer, authoring, engine, contracts })
    installFrozenRuleAuthoringConsumer(consumer, authoring, engine, contracts, run)
  }
  new Function('resolve', 'fixtureRoot', 'cpSync', 'root', 'readdirSync', 'npmArtifacts', 'writeFileSync', 'readFileSync', 'installFrozenRuleAuthoringConsumer', 'artifact', 'packedArtifact', 'run', body.slice(start, end))(
    resolve, state.scratch, copy, root, select, state.npmArtifacts, writeFileSync, readFileSync, installSelected, state.paths[0], (prefix, label) => {
      assert.equal(prefix, 'harborline-software-contracts-')
      assert.equal(label, 'Rule Authoring consumer')
      return state.paths[2]
    }, state.run)
  // This is the next control-flow point, not execution of Node or the external product exercise.
  state.reachedNodeInvocation = true
}

test('the actual Rule Authoring caller uses the captured triple graph, exact selected archives and three frozen operations', t => {
  const state = ruleAuthoringState(t)
  runRuleAuthoringCaller(state)
  assert.equal(createHash('sha256').update(ruleAuthoringCapturedLock).digest('hex'), 'f222eb0bebfaf21f29182c7f5d21de115bafd9dfa604e65d849cfa06be3320f1')
  assert.equal(Buffer.byteLength(ruleAuthoringCapturedLock), 2401)
  assert.equal(createHash('sha256').update(ruleAuthoringCapturedManifest).digest('hex'), '4da9196c034f1f988f6e4e8d5cc6282c2edea9c0aa5f8d0c4db29fb33e720c47')
  assert.deepEqual(state.copies, [{ from: resolve(root, 'tests/package-consumers/forms-contracts-npm'), to: state.consumer, options: { recursive: true } }])
  assert.deepEqual(state.lookups, [state.npmArtifacts])
  assert.deepEqual(state.helperCalls, [{ consumer: state.consumer, authoring: state.paths[0], engine: state.paths[1], contracts: state.paths[2] }])
  assert.deepEqual(state.calls, [
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--ignore-scripts'], cwd: state.consumer },
    { command: 'pnpm', args: ['add', '--lockfile-only', '--offline', '--ignore-scripts', 'file:.packed/rule-authoring.tgz', 'file:.packed/rule-engine.tgz', 'file:.packed/contracts.tgz'], cwd: state.consumer },
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], cwd: state.consumer },
  ])
  for (const [index, name] of ['rule-authoring', 'rule-engine', 'contracts'].entries()) {
    assert.deepEqual(readFileSync(resolve(state.consumer, `.packed/${name}.tgz`)), state.bytes[index])
    assert.deepEqual(readFileSync(state.paths[index]), state.bytes[index])
  }
  assert.equal(createHash('sha256').update(readFileSync(resolve(state.consumer, 'exercise.mjs'))).digest('hex'), '403da6fa301c09ae3323ef5de878d691ffe04cd0c387a519b5d9a70959593a8f')
  assert.equal(readFileSync(resolve(state.consumer, 'pnpm-workspace.yaml'), 'utf8'), 'packages:\n  - .\nnodeLinker: hoisted\nautoInstallPeers: false\n')
  assert.equal(state.reachedNodeInvocation, true)
})

test('Rule Authoring binds each actual archive version and its selected optional runtime peer', t => {
  const packedManifests = structuredClone(ruleAuthoringTestManifests)
  packedManifests[0].version = '0.2.0-alpha.1'
  packedManifests[0].peerDependencies['@harborline-software/rule-engine'] = '0.3.0-alpha.2'
  packedManifests[1].version = '0.3.0-alpha.2'
  const state = ruleAuthoringState(t, { packedManifests })
  runRuleAuthoringCaller(state)
  const lock = readFileSync(resolve(state.consumer, 'pnpm-lock.yaml'), 'utf8')
  assert.match(lock, /    version: 0\.2\.0-alpha\.1\n/)
  assert.match(lock, /    version: 0\.3\.0-alpha\.2\n/)
  assert.ok(lock.includes("'@harborline-software/rule-engine': 0.3.0-alpha.2\n"))
  assert.equal(state.calls.length, 3)
})

for (const engineNames of [[], ['harborline-software-rule-engine-a.tgz', 'harborline-software-rule-engine-b.tgz']]) test(`Rule Authoring requires exactly one packed engine when ${engineNames.length} match`, t => {
  const state = ruleAuthoringState(t)
  assert.throws(() => runRuleAuthoringCaller(state, { engineNames }), /requires exactly one packed rule-engine artifact/)
  assert.deepEqual(state.helperCalls, [])
  assert.equal(state.calls.length, 0)
  assert.equal(existsSync(resolve(state.consumer, 'exercise.mjs')), false)
  assert.equal(state.reachedNodeInvocation, false)
})

test('Rule Authoring ordinary baseline copy failure stops before engine selection or helper invocation', t => {
  const state = ruleAuthoringState(t)
  assert.throws(() => runRuleAuthoringCaller(state, { failCopy: true }), error => error === state.copyFailure)
  assert.deepEqual(state.lookups, [])
  assert.deepEqual(state.helperCalls, [])
  assert.equal(state.calls.length, 0)
  assert.equal(existsSync(resolve(state.consumer, 'exercise.mjs')), false)
  assert.equal(state.reachedNodeInvocation, false)
})

for (const failAt of [1, 2, 3]) test(`Rule Authoring command ${failAt} failure propagates without fallback or exercise invocation`, t => {
  const state = ruleAuthoringState(t, { failAt })
  assert.throws(() => runRuleAuthoringCaller(state), error => error === state.failure)
  assert.equal(state.calls.length, failAt)
  assert.equal(state.reachedNodeInvocation, false)
  if (failAt === 1) assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
})

for (const [file, contents, expected] of [
  ['package.json', '{"private":true,"type":"module","packageManager":"pnpm@11.1.4"}', /Rule Authoring consumer manifest changed/],
  ['pnpm-lock.yaml', 'stale\n', /Rule Authoring empty baseline changed/],
  ['pnpm-workspace.yaml', 'packages: []\n', /Rule Authoring consumer workspace changed/],
]) test(`Rule Authoring unsupported baseline ${file} refuses before installation or staging`, t => {
  const state = ruleAuthoringState(t, { afterCopy: ({ consumer }) => writeFileSync(resolve(consumer, file), contents) })
  assert.throws(() => runRuleAuthoringCaller(state), expected)
  assert.equal(state.calls.length, 0)
  assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
  assert.equal(state.reachedNodeInvocation, false)
})

for (const file of ['package.json', 'pnpm-lock.yaml', 'pnpm-workspace.yaml']) test(`Rule Authoring baseline installation cannot repair ${file}`, t => {
  const state = ruleAuthoringState(t, { afterCall: (number, { consumer }) => { if (number === 1) writeFileSync(resolve(consumer, file), 'changed\n') } })
  assert.throws(() => runRuleAuthoringCaller(state), /Rule Authoring baseline install rewrote/)
  assert.equal(state.calls.length, 1)
  assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
  assert.equal(state.reachedNodeInvocation, false)
})

for (const index of [0, 1, 2]) {
  for (const [label, mutate, expected] of [
    ['identity', value => ({ ...value, name: '@harborline-software/other' }), /identity mismatch/],
    ['version', value => ({ ...value, version: 'unsafe\nversion' }), /unsupported Rule Authoring package version/],
    ...['dependencies', 'optionalDependencies'].map(field => [field, value => ({ ...value, [field]: { other: '1.0.0' } }), new RegExp(`Rule Authoring packed ${field} must be empty`)]),
  ]) test(`Rule Authoring selected archive ${index} ${label} refuses before derivation`, t => {
    const packedManifests = structuredClone(ruleAuthoringTestManifests)
    packedManifests[index] = mutate(packedManifests[index])
    const state = ruleAuthoringState(t, { packedManifests })
    assert.throws(() => runRuleAuthoringCaller(state), expected)
    assert.equal(state.calls.length, 1)
    assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
    assert.equal(state.reachedNodeInvocation, false)
  })
  test(`Rule Authoring selected archive ${index} malformed gzip refuses before derivation`, t => {
    const state = ruleAuthoringState(t)
    writeFileSync(state.paths[index], 'not a gzip archive')
    assert.throws(() => runRuleAuthoringCaller(state))
    assert.equal(state.calls.length, 1)
    assert.equal(state.reachedNodeInvocation, false)
  })
  test(`Rule Authoring selected archive ${index} corrupt tar header refuses before derivation`, t => {
    const state = ruleAuthoringState(t)
    const tar = gunzipSync(state.bytes[index]); tar[0] ^= 1
    writeFileSync(state.paths[index], gzipSync(tar))
    assert.throws(() => runRuleAuthoringCaller(state), /packed header checksum/)
    assert.equal(state.calls.length, 1)
    assert.equal(state.reachedNodeInvocation, false)
  })
}

for (const [label, field, value, expected] of [
  ['missing peer', 'peerDependencies', undefined, /exactly the selected runtime peer/],
  ['extra peer', 'peerDependencies', { '@harborline-software/rule-engine': '0.1.0-alpha.0', other: '1.0.0' }, /exactly the selected runtime peer/],
  ['changed peer version', 'peerDependencies', { '@harborline-software/rule-engine': '0.1.0-alpha.1' }, /exactly the selected runtime peer/],
  ['registry range peer', 'peerDependencies', { '@harborline-software/rule-engine': '^0.1.0' }, /exactly the selected runtime peer/],
  ['missing peer metadata', 'peerDependenciesMeta', undefined, /runtime peer must be exactly optional/],
  ['required peer', 'peerDependenciesMeta', { '@harborline-software/rule-engine': { optional: false } }, /runtime peer must be exactly optional/],
  ['string optional flag', 'peerDependenciesMeta', { '@harborline-software/rule-engine': { optional: 'true' } }, /runtime peer must be exactly optional/],
  ['extra peer metadata', 'peerDependenciesMeta', { '@harborline-software/rule-engine': { optional: true }, other: { optional: true } }, /runtime peer must be exactly optional/],
]) test(`Rule Authoring ${label} refuses before staging or derivation`, t => {
  const packedManifests = structuredClone(ruleAuthoringTestManifests)
  packedManifests[0][field] = value
  const state = ruleAuthoringState(t, { packedManifests })
  assert.throws(() => runRuleAuthoringCaller(state), expected)
  assert.equal(state.calls.length, 1)
  assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
  assert.equal(state.reachedNodeInvocation, false)
})

for (const field of ['peerDependencies', 'peerDependenciesMeta']) test(`Rule Authoring runtime archive additional ${field} refuses before staging`, t => {
  const packedManifests = structuredClone(ruleAuthoringTestManifests)
  packedManifests[1][field] = { other: field === 'peerDependencies' ? '1.0.0' : { optional: true } }
  const state = ruleAuthoringState(t, { packedManifests })
  assert.throws(() => runRuleAuthoringCaller(state), field === 'peerDependencies' ? /runtime must declare exactly the selected Contracts peer/ : /runtime Contracts peer must be required/)
  assert.equal(state.calls.length, 1)
  assert.equal(state.reachedNodeInvocation, false)
})

for (const [label, mutateLock, expected] of [
  ['authoring integrity', value => value.replace(/integrity: sha512-[^,]+/, 'integrity: wrong'), /local tarball integrity\/path mismatch/],
  ['runtime integrity', value => value.replace(/integrity: sha512-[^,]+, tarball: file:\.packed\/rule-engine.tgz/, 'integrity: wrong, tarball: file:.packed/rule-engine.tgz'), /local tarball integrity\/path mismatch/],
  ['path', value => value.replaceAll('file:.packed/rule-authoring.tgz', 'file:../rule-authoring.tgz'), /local reference changed its tarball path/],
  ['authoring version', value => value.replace('    version: 0.1.0-alpha.0\n', '    version: 9.9.9\n'), /local package version mismatch/],
  ['runtime version', value => value.replace('tarball: file:.packed/rule-engine.tgz}\n    version: 0.1.0-alpha.0\n', 'tarball: file:.packed/rule-engine.tgz}\n    version: 9.9.9\n'), /local package version mismatch/],
  ['settings', value => value.replace('autoInstallPeers: false', 'autoInstallPeers: true'), /settings changed/],
  ['extra importer', value => value.replace('importers:\n', 'importers:\n\n  other: {}\n'), /only the consumer importer/],
  ['extra package', value => value.replace('packages:\n', 'packages:\n\n  other@1.0.0: {}\n'), /unexpected packages entry/],
  ['extra snapshot', value => value + '\n  other@1.0.0: {}\n', /unexpected snapshots entry/],
  ['peer declaration', value => value.replace("      '@harborline-software/rule-engine': 0.1.0-alpha.0\n", "      '@harborline-software/rule-engine': 9.9.9\n"), /local peer declarations changed/],
  ['peer optionality', value => value.replace('        optional: true', '        optional: false'), /unsupported peer metadata/],
  ['wrong peer context', value => value.replaceAll('(@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz))', '(@harborline-software/rule-engine@file:.packed/other.tgz)'), /local peer context target changed/],
  ['missing peer context', value => value.replaceAll('(@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz))', ''), /local peer context changed/],
  ['required snapshot edge', value => value.replace('    optionalDependencies:', '    dependencies:'), /local dependency optionality changed/],
  ['wrong snapshot target', value => value.replace("      '@harborline-software/rule-engine': file:.packed/rule-engine.tgz", "      '@harborline-software/rule-engine': file:.packed/rule-authoring.tgz"), /local dependency target changed/],
]) test(`Rule Authoring changed triple lock ${label} refuses before final install`, t => {
  const state = ruleAuthoringState(t, { mutateLock })
  assert.throws(() => runRuleAuthoringCaller(state), expected)
  assert.equal(state.calls.length, 2)
  assert.equal(state.reachedNodeInvocation, false)
})

test('Rule Authoring derived manifest cannot add an unadmitted package', t => {
  const state = ruleAuthoringState(t, { mutateManifest: value => ({ ...value, dependencies: { ...value.dependencies, other: '1.0.0' } }) })
  assert.throws(() => runRuleAuthoringCaller(state), /Rule Authoring derived manifest changed/)
  assert.equal(state.calls.length, 2)
  assert.equal(state.reachedNodeInvocation, false)
})

test('Rule Authoring staging refuses an existing packed target without overwriting it', t => {
  const state = ruleAuthoringState(t, { afterCall: (number, { consumer }) => {
    if (number === 1) {
      mkdirSync(resolve(consumer, '.packed'))
      writeFileSync(resolve(consumer, '.packed/rule-authoring.tgz'), 'existing owner bytes')
    }
  } })
  assert.throws(() => runRuleAuthoringCaller(state), error => error.code === 'EEXIST')
  assert.equal(readFileSync(resolve(state.consumer, '.packed/rule-authoring.tgz'), 'utf8'), 'existing owner bytes')
  assert.equal(state.calls.length, 1)
  assert.equal(state.reachedNodeInvocation, false)
})

const ruleAuthoringPreservationCases = [
  ['workspace', ({ consumer }) => writeFileSync(resolve(consumer, 'pnpm-workspace.yaml'), 'changed\n'), /Rule Authoring workspace changed/],
  ...[0, 1, 2].map(index => [`producer archive ${index}`, ({ paths }) => writeFileSync(paths[index], 'changed'), /Rule Authoring producer tarball changed/]),
  ...['rule-authoring', 'rule-engine', 'contracts'].map(name => [`staged ${name}`, ({ consumer }) => writeFileSync(resolve(consumer, `.packed/${name}.tgz`), 'changed'), /Rule Authoring staged tarball changed/]),
]
for (const number of [2, 3]) for (const [label, change, expected] of ruleAuthoringPreservationCases) test(`Rule Authoring command ${number} cannot change ${label}`, t => {
  const state = ruleAuthoringState(t, { afterCall: (count, context) => { if (count === number) change(context) } })
  assert.throws(() => runRuleAuthoringCaller(state), expected)
  assert.equal(state.calls.length, number)
  assert.equal(state.reachedNodeInvocation, false)
})

for (const [file, expected] of [['pnpm-lock.yaml', /Rule Authoring final install rewrote lock/], ['package.json', /Rule Authoring final install rewrote manifest/]]) test(`Rule Authoring final install cannot rewrite ${file}`, t => {
  const state = ruleAuthoringState(t, { afterCall: (number, { consumer }) => { if (number === 3) writeFileSync(resolve(consumer, file), 'changed\n') } })
  assert.throws(() => runRuleAuthoringCaller(state), expected)
  assert.equal(state.calls.length, 3)
  assert.equal(state.reachedNodeInvocation, false)
})

import { readdirSync } from 'node:fs'

const calculationsCopiedInputs = [
  { file: 'package.json', bytes: 77, sha256: '1485792c7933b0c95a7f8a194307c86bc14676bd583a1e65b6a66a9c2f1dc2d1' },
  { file: 'pnpm-lock.yaml', bytes: 115, sha256: '878a7b60746fb21ffe11029327f29ea50cfc840bfeb9e0b3cbb46a77f828b7e7' },
  { file: 'pnpm-workspace.yaml', bytes: 60, sha256: 'c8b2f4c55a47976cdc45d898428f1127aafd3ba753d69fa3f18cde4c2ff59636' },
  { file: 'exercise.mjs', bytes: 3841, sha256: '0d3aec790e2e738f7748733cca44d3e178dd5ba90a1387a664492d942d8ffab0' },
  { file: 'authoring-verdict-cases.json', bytes: 8317, sha256: 'f568812addd13ebfb0e0c18099d8caa41baa542665c15f77eccec5079e2cbd61' },
  { file: 'definition-intent-cases.json', bytes: 33844, sha256: 'ad455c51c3bfb0ddc98e44275e24d33602ee463fc10a05b782cd0339fa2bfa0a' },
]

function calculationsState(t, { failAt } = {}) {
  const scratch = mkdtempSync(resolve(tmpdir(), 'harborline-calculations-frozen-'))
  t.after(() => rmSync(scratch, { recursive: true, force: true }))
  const consumer = resolve(scratch, 'calculations-npm-consumer')
  const npmArtifacts = resolve(scratch, 'npm')
  mkdirSync(npmArtifacts)
  const bytes = ruleAuthoringTestManifests.map(archive)
  const paths = ['rule-authoring', 'rule-engine', 'contracts'].map((name, index) => {
    const path = resolve(npmArtifacts, `harborline-software-${name}-unit.tgz`)
    writeFileSync(path, bytes[index])
    return path
  })
  for (const ignored of ['unrelated.tgz', 'harborline-software-rule-authoring-ignored.txt', 'harborline-software-rule-engine-ignored.txt']) {
    writeFileSync(resolve(npmArtifacts, ignored), 'ignored by the real packed resolver')
  }
  const state = {
    scratch, consumer, npmArtifacts, paths, bytes, calls: [], copies: [], lookups: [], helperCalls: [],
    failure: new Error('ordinary Calculations pnpm failure'),
    copyFailure: new Error('ordinary Calculations copy failure'),
    nextBoundaryReached: false,
  }
  // This runner closes over the real Calculations path; no standalone state property is rebound.
  state.run = (command, args, options) => {
    state.calls.push({ command, args, cwd: options.cwd })
    assert.equal(options.cwd, consumer, 'all three operations target the Calculations scratch consumer')
    if (state.calls.length === failAt) throw state.failure
    if (state.calls.length === 1) {
      state.initialInputs = calculationsCopiedInputs.map(({ file }) => {
        const data = readFileSync(resolve(consumer, file))
        return { file, bytes: data.length, sha256: createHash('sha256').update(data).digest('hex') }
      })
    }
    if (state.calls.length === 2) {
      const sris = bytes.map(data => `sha512-${createHash('sha512').update(data).digest('base64')}`)
      writeFileSync(resolve(consumer, 'pnpm-lock.yaml'), ruleAuthoringCapturedSris.reduce((text, sri, index) => text.replace(sri, sris[index]), ruleAuthoringCapturedLock))
      writeFileSync(resolve(consumer, 'package.json'), ruleAuthoringCapturedManifest)
    }
    return ''
  }
  return state
}

function runCalculationsRendererInstall(state, { failCopy } = {}) {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const body = source.slice(source.indexOf('function verifyCalculationsCapability()'), source.indexOf('function verifyViewsCapability()'))
  const start = body.indexOf('  const corpusSource = ')
  const end = body.indexOf('  assertPackedInstall(')
  assert.ok(start > 0 && end > start, 'complete actual Calculations renderer setup/install boundary')
  assert.ok(body.slice(end).startsWith("  assertPackedInstall(rendererConsumer, ['rule-authoring', 'rule-engine', 'contracts'], 'Calculations renderer')"))
  assert.ok(body.indexOf('proofLines(clientRun(rendererConsumer)') > end)
  assert.ok(body.indexOf('  const engineConsumer = ') > end)
  assert.doesNotMatch(body, /installPackedArtifacts\(|package-lock=false|run\('npm', \['install'/, 'no mutable Calculations install fallback')
  const resolverStart = source.indexOf('function packedArtifact(')
  const resolverEnd = source.indexOf('function installPackedArtifacts(', resolverStart)
  assert.ok(resolverStart > 0 && resolverEnd > resolverStart, 'real shared exactly-one packed artifact resolver')
  const packedArtifact = new Function('readdirSync', 'npmArtifacts', 'resolve', `${source.slice(resolverStart, resolverEnd)}\nreturn packedArtifact`)(
    readdirSync, state.npmArtifacts, resolve)
  const lookup = (prefix, label) => {
    state.lookups.push({ prefix, label })
    return packedArtifact(prefix, label)
  }
  const copy = (type, from, to, options) => {
    state.copies.push(type === 'directory' ? { type, from, to, options } : { type, from, to })
    if (state.copies.length === failCopy) throw state.copyFailure
    if (type === 'directory') cpSync(from, to, options)
    else copyFileSync(from, to)
  }
  const install = (consumer, authoring, engine, contracts, run) => {
    state.helperCalls.push({ consumer, authoring, engine, contracts, run })
    installFrozenRuleAuthoringConsumer(consumer, authoring, engine, contracts, run)
  }
  const nextBoundary = Symbol('stop before installed assertion, client/proof and native work')
  const setup = new Function('root', 'fixtureRoot', 'resolve', 'readFileSync', 'cpSync', 'copyFileSync', 'installFrozenRuleAuthoringConsumer', 'packedArtifact', 'run', 'nextBoundary', `${body.slice(start, end)}\nthrow nextBoundary`)
  try {
    setup(root, state.scratch, resolve, readFileSync,
      (from, to, options) => copy('directory', from, to, options),
      (from, to) => copy('file', from, to), install, lookup, state.run, nextBoundary)
  } catch (error) {
    if (error !== nextBoundary) throw error
    // This sentinel records control flow only; no installed/client/proof/native code runs.
    state.nextBoundaryReached = true
  }
}

test('the actual Calculations renderer copies six literal inputs in four operations and installs the real selected triple', t => {
  const state = calculationsState(t)
  runCalculationsRendererInstall(state)
  assert.equal(state.initialInputs.length, 6)
  assert.deepEqual(state.initialInputs, calculationsCopiedInputs)
  assert.equal(state.copies.length, 4)
  assert.deepEqual(state.copies, [
    { type: 'directory', from: resolve(root, 'tests/package-consumers/forms-contracts-npm'), to: state.consumer, options: { recursive: true } },
    { type: 'file', from: resolve(root, 'tests/package-consumers/calculations-npm/exercise.mjs'), to: resolve(state.consumer, 'exercise.mjs') },
    { type: 'file', from: resolve(root, 'conformance/hlp.foundation.rule-authoring/authoring-verdict-cases.json'), to: resolve(state.consumer, 'authoring-verdict-cases.json') },
    { type: 'file', from: resolve(root, 'conformance/hlp.foundation.rule-authoring/definition-intent-cases.json'), to: resolve(state.consumer, 'definition-intent-cases.json') },
  ])
  assert.deepEqual(state.lookups, [
    { prefix: 'harborline-software-rule-authoring-', label: 'Calculations renderer' },
    { prefix: 'harborline-software-rule-engine-', label: 'Calculations renderer' },
    { prefix: 'harborline-software-contracts-', label: 'Calculations renderer' },
  ])
  assert.deepEqual(state.helperCalls, [{ consumer: state.consumer, authoring: state.paths[0], engine: state.paths[1], contracts: state.paths[2], run: state.run }])
  assert.deepEqual(state.calls, [
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--ignore-scripts'], cwd: state.consumer },
    { command: 'pnpm', args: ['add', '--lockfile-only', '--offline', '--ignore-scripts', 'file:.packed/rule-authoring.tgz', 'file:.packed/rule-engine.tgz', 'file:.packed/contracts.tgz'], cwd: state.consumer },
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], cwd: state.consumer },
  ])
  for (const [index, name] of ['rule-authoring', 'rule-engine', 'contracts'].entries()) {
    assert.deepEqual(readFileSync(state.paths[index]), state.bytes[index])
    assert.deepEqual(readFileSync(resolve(state.consumer, `.packed/${name}.tgz`)), state.bytes[index])
  }
  assert.equal(state.nextBoundaryReached, true)
})

for (const failCopy of [1, 2, 3, 4]) test(`Calculations copy operation ${failCopy} failure stops before selection or installation`, t => {
  const state = calculationsState(t)
  assert.throws(() => runCalculationsRendererInstall(state, { failCopy }), error => error === state.copyFailure)
  assert.equal(state.copies.length, failCopy)
  assert.deepEqual(state.lookups, [])
  assert.deepEqual(state.helperCalls, [])
  assert.equal(state.calls.length, 0)
  assert.equal(state.nextBoundaryReached, false)
})

for (const [index, name] of ['rule-authoring', 'rule-engine', 'contracts'].entries()) {
  for (const matches of [0, 2]) test(`Calculations real resolver refuses ${matches} matching ${name} archives before installation`, t => {
    const state = calculationsState(t)
    if (matches === 0) rmSync(state.paths[index])
    else writeFileSync(resolve(state.npmArtifacts, `harborline-software-${name}-duplicate.tgz`), state.bytes[index])
    assert.throws(() => runCalculationsRendererInstall(state), /Calculations renderer requires exactly one packed/)
    assert.equal(state.copies.length, 4)
    assert.equal(state.lookups.length, index + 1)
    assert.deepEqual(state.helperCalls, [])
    assert.equal(state.calls.length, 0)
    assert.equal(state.nextBoundaryReached, false)
  })
}

for (const failAt of [1, 2, 3]) test(`Calculations frozen command ${failAt} failure propagates before the next boundary`, t => {
  const state = calculationsState(t, { failAt })
  assert.throws(() => runCalculationsRendererInstall(state), error => error === state.failure)
  assert.equal(state.calls.length, failAt)
  assert.equal(state.helperCalls.length, 1)
  assert.equal(state.nextBoundaryReached, false)
  if (failAt === 1) assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
})

// pnpm 11.1.3 capture includes the required nested Contracts peer; only archive integrities change.
const viewsCapturedLock = "lockfileVersion: '9.0'\n\nsettings:\n  autoInstallPeers: false\n  excludeLinksFromLockfile: false\n\nimporters:\n\n  .:\n    dependencies:\n      '@harborline-software/contracts':\n        specifier: file:.packed\\contracts.tgz\n        version: file:.packed/contracts.tgz\n      '@harborline-software/rule-authoring':\n        specifier: file:.packed\\rule-authoring.tgz\n        version: file:.packed/rule-authoring.tgz(@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz))\n      '@harborline-software/rule-engine':\n        specifier: file:.packed\\rule-engine.tgz\n        version: file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)\n      '@harborline-software/ui-react':\n        specifier: file:.packed\\ui-react.tgz\n        version: file:.packed/ui-react.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)(@harborline-software/rule-authoring@file:.packed/rule-authoring.tgz(@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)))(@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz))(@types/react@19.2.18)(react-dom@19.2.8(react@19.2.8))(react@19.2.8)\n      '@radix-ui/react-slot':\n        specifier: 1.3.3\n        version: 1.3.3(@types/react@19.2.18)(react@19.2.8)\n      '@types/react':\n        specifier: 19.2.18\n        version: 19.2.18\n      clsx:\n        specifier: 2.1.1\n        version: 2.1.1\n      react:\n        specifier: 19.2.8\n        version: 19.2.8\n      react-dom:\n        specifier: 19.2.8\n        version: 19.2.8(react@19.2.8)\n      tailwind-merge:\n        specifier: 3.6.0\n        version: 3.6.0\n      typescript:\n        specifier: 5.9.3\n        version: 5.9.3\n\npackages:\n\n  '@harborline-software/contracts@file:.packed/contracts.tgz':\n    resolution: {integrity: sha512-d3ZFKmEI5/+lpfNjrln+5JMmdYk55OqsLUP0laj1Kf7dhXvVaE3+6cIMdYqKiC26ECU0q9mR94Dm0H7VXQ1Mbw==, tarball: file:.packed/contracts.tgz}\n    version: 0.0.0-alpha.0\n\n  '@harborline-software/rule-authoring@file:.packed/rule-authoring.tgz':\n    resolution: {integrity: sha512-DFxoLjFfqf7V5OBCtx8O1mhWTe14c9cY9SNQpUawqNoCl1IBrL/xkn0cFHtCZhJP8JdlPAcnBy/G5DUoJM/trA==, tarball: file:.packed/rule-authoring.tgz}\n    version: 0.1.0-alpha.0\n    peerDependencies:\n      '@harborline-software/rule-engine': 0.1.0-alpha.0\n    peerDependenciesMeta:\n      '@harborline-software/rule-engine':\n        optional: true\n\n  '@harborline-software/rule-engine@file:.packed/rule-engine.tgz':\n    resolution: {integrity: sha512-JmBTJqoCJ6MpiJjagBn4psWMoDr6ORv0dyaA+6S0UH3PsMxMzJaYs8oggZHpcsJUIrVYrJGCKl5i0jNCqbWWbA==, tarball: file:.packed/rule-engine.tgz}\n    version: 0.1.0-alpha.0\n    peerDependencies:\n      '@harborline-software/contracts': 0.0.0-alpha.0\n\n  '@harborline-software/ui-react@file:.packed/ui-react.tgz':\n    resolution: {integrity: sha512-Sz2mVmewp1C8rNBvNXktSq4b1UIbnfPG9QiBGGHrMk82OZHBu5Jl5jrIsEZqYrWH85mnYPCZHumUmFwu0rAiMQ==, tarball: file:.packed/ui-react.tgz}\n    version: 0.8.0-alpha\n    peerDependencies:\n      '@harborline-software/contracts': 0.0.0-alpha.0\n      '@harborline-software/rule-authoring': 0.1.0-alpha.0\n      '@harborline-software/rule-engine': 0.1.0-alpha.0\n      react: ^18.0.0 || ^19.0.0\n      react-dom: ^18.0.0 || ^19.0.0\n    peerDependenciesMeta:\n      '@harborline-software/contracts':\n        optional: true\n      '@harborline-software/rule-authoring':\n        optional: true\n      '@harborline-software/rule-engine':\n        optional: true\n\n  '@radix-ui/react-compose-refs@1.1.5':\n    resolution: {integrity: sha512-+48PbAAbq3didjJxa+OaWY2ZwgAKsNiRGyeHKszblZMQ+kcpd9pAaT11cMkGEie0vsOi3QdeTE6d5Fe3Gn61kA==}\n    peerDependencies:\n      '@types/react': '*'\n      react: ^16.8 || ^17.0 || ^18.0 || ^19.0 || ^19.0.0-rc\n    peerDependenciesMeta:\n      '@types/react':\n        optional: true\n\n  '@radix-ui/react-slot@1.3.3':\n    resolution: {integrity: sha512-qx7oqnYbxnK9kYI9m317qmFmEgo6ywqWvbTogdj7cL9p3/yx4M48p7Rnw5z3H890cL/ow/EeWJsuTykeZVXP5Q==}\n    peerDependencies:\n      '@types/react': '*'\n      react: ^16.8 || ^17.0 || ^18.0 || ^19.0 || ^19.0.0-rc\n    peerDependenciesMeta:\n      '@types/react':\n        optional: true\n\n  '@types/react@19.2.18':\n    resolution: {integrity: sha512-AnzbBERsrLKtk2XSfTbYRLjQPdy116Sty4q+T+Bp3IC4l6jNBvreVPAHmpq9qhXQM7CXZPjLVmGMw9sy+hxQ3w==}\n\n  clsx@2.1.1:\n    resolution: {integrity: sha512-eYm0QWBtUrBWZWG0d386OGAw16Z995PiOVo2B7bjWSbHedGl5e0ZWaq65kOGgUSNesEIDkB9ISbTg/JK9dhCZA==}\n    engines: {node: '>=6'}\n\n  csstype@3.2.3:\n    resolution: {integrity: sha512-z1HGKcYy2xA8AGQfwrn0PAy+PB7X/GSj3UVJW9qKyn43xWa+gl5nXmU4qqLMRzWVLFC8KusUX8T/0kCiOYpAIQ==}\n\n  react-dom@19.2.8:\n    resolution: {integrity: sha512-rVprimfGBG3DR+Tq0IQG2DT5PxKth1WIGDmj5yPmlzr4YBe7uyE+Du4oVqTDXZSHGGGXRtTJEGSSePyQCMBglQ==}\n    peerDependencies:\n      react: ^19.2.8\n\n  react@19.2.8:\n    resolution: {integrity: sha512-PWaYA1L/q9u2u7xYQi+Y3L3Yfnie7XyLeaJICV1MGD6LprsBxcAqGjYyr0eY3p+QdsA+x/Irkt4Qif8D63+Sbw==}\n    engines: {node: '>=0.10.0'}\n\n  scheduler@0.27.0:\n    resolution: {integrity: sha512-eNv+WrVbKu1f3vbYJT/xtiF5syA5HPIMtf9IgY/nKg0sWqzAUEvqY/xm7OcZc/qafLx/iO9FgOmeSAp4v5ti/Q==}\n\n  tailwind-merge@3.6.0:\n    resolution: {integrity: sha512-uxL7qAVQriqRQPAyK3pj66VqskWqoZ37PW94jwOTwNfq/z9oyu1V+eqrZqtR2+fCiXdYOZe/Modt8GtvqNzu+w==}\n\n  typescript@5.9.3:\n    resolution: {integrity: sha512-jl1vZzPDinLr9eUt3J/t7V6FgNEw9QjvBPdysz9KfQDD41fQrC2Y4vKQdiaUpFT4bXlb1RHhLpp8wtm6M5TgSw==}\n    engines: {node: '>=14.17'}\n    hasBin: true\n\nsnapshots:\n\n  '@harborline-software/contracts@file:.packed/contracts.tgz': {}\n\n  '@harborline-software/rule-authoring@file:.packed/rule-authoring.tgz(@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz))':\n    optionalDependencies:\n      '@harborline-software/rule-engine': file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)\n\n  '@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)':\n    dependencies:\n      '@harborline-software/contracts': file:.packed/contracts.tgz\n\n  '@harborline-software/ui-react@file:.packed/ui-react.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)(@harborline-software/rule-authoring@file:.packed/rule-authoring.tgz(@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)))(@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz))(@types/react@19.2.18)(react-dom@19.2.8(react@19.2.8))(react@19.2.8)':\n    dependencies:\n      '@radix-ui/react-slot': 1.3.3(@types/react@19.2.18)(react@19.2.8)\n      clsx: 2.1.1\n      react: 19.2.8\n      react-dom: 19.2.8(react@19.2.8)\n      tailwind-merge: 3.6.0\n    optionalDependencies:\n      '@harborline-software/contracts': file:.packed/contracts.tgz\n      '@harborline-software/rule-authoring': file:.packed/rule-authoring.tgz(@harborline-software/rule-engine@file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz))\n      '@harborline-software/rule-engine': file:.packed/rule-engine.tgz(@harborline-software/contracts@file:.packed/contracts.tgz)\n    transitivePeerDependencies:\n      - '@types/react'\n\n  '@radix-ui/react-compose-refs@1.1.5(@types/react@19.2.18)(react@19.2.8)':\n    dependencies:\n      react: 19.2.8\n    optionalDependencies:\n      '@types/react': 19.2.18\n\n  '@radix-ui/react-slot@1.3.3(@types/react@19.2.18)(react@19.2.8)':\n    dependencies:\n      '@radix-ui/react-compose-refs': 1.1.5(@types/react@19.2.18)(react@19.2.8)\n      react: 19.2.8\n    optionalDependencies:\n      '@types/react': 19.2.18\n\n  '@types/react@19.2.18':\n    dependencies:\n      csstype: 3.2.3\n\n  clsx@2.1.1: {}\n\n  csstype@3.2.3: {}\n\n  react-dom@19.2.8(react@19.2.8):\n    dependencies:\n      react: 19.2.8\n      scheduler: 0.27.0\n\n  react@19.2.8: {}\n\n  scheduler@0.27.0: {}\n\n  tailwind-merge@3.6.0: {}\n\n  typescript@5.9.3: {}\n"
const viewsCapturedManifest = "{\n  \"name\": \"harborline-platform-npm-clean-consumer\",\n  \"private\": true,\n  \"type\": \"module\",\n  \"packageManager\": \"pnpm@11.1.3\",\n  \"justifications\": {\n    \"@types/react\": \"R-0086\",\n    \"react\": \"R-0080\",\n    \"react-dom\": \"R-0080\",\n    \"typescript\": \"R-0086\",\n    \"@radix-ui/react-slot\": \"R-0081\",\n    \"clsx\": \"R-0082\",\n    \"tailwind-merge\": \"R-0082\"\n  },\n  \"dependencies\": {\n    \"@harborline-software/contracts\": \u0022file:.packed\\\\contracts.tgz\",\n    \"@harborline-software/rule-authoring\": \u0022file:.packed\\\\rule-authoring.tgz\",\n    \"@harborline-software/rule-engine\": \u0022file:.packed\\\\rule-engine.tgz\",\n    \"@harborline-software/ui-react\": \u0022file:.packed\\\\ui-react.tgz\",\n    \"@radix-ui/react-slot\": \"1.3.3\",\n    \"@types/react\": \"19.2.18\",\n    \"clsx\": \"2.1.1\",\n    \"react\": \"19.2.8\",\n    \"react-dom\": \"19.2.8\",\n    \"tailwind-merge\": \"3.6.0\",\n    \"typescript\": \"5.9.3\"\n  }\n}\n"
const viewsCapturedSris = ["sha512-Sz2mVmewp1C8rNBvNXktSq4b1UIbnfPG9QiBGGHrMk82OZHBu5Jl5jrIsEZqYrWH85mnYPCZHumUmFwu0rAiMQ==", "sha512-d3ZFKmEI5/+lpfNjrln+5JMmdYk55OqsLUP0laj1Kf7dhXvVaE3+6cIMdYqKiC26ECU0q9mR94Dm0H7VXQ1Mbw==", "sha512-JmBTJqoCJ6MpiJjagBn4psWMoDr6ORv0dyaA+6S0UH3PsMxMzJaYs8oggZHpcsJUIrVYrJGCKl5i0jNCqbWWbA==", "sha512-DFxoLjFfqf7V5OBCtx8O1mhWTe14c9cY9SNQpUawqNoCl1IBrL/xkn0cFHtCZhJP8JdlPAcnBy/G5DUoJM/trA=="]
const viewsCopiedInputs = [
  {
    "file": "package.json",
    "bytes": 576,
    "sha256": "1ed842c0c5da0e6c0906eb245c5aea075de6fafd45be722b0bb1a774c3fd139d"
  },
  {
    "file": "pnpm-lock.yaml",
    "bytes": 3390,
    "sha256": "2a9aa0df9998309ba77aab5f9b9e7d7a759f2812d7d8e04158c2a08708fee51e"
  },
  {
    "file": "pnpm-workspace.yaml",
    "bytes": 60,
    "sha256": "c8b2f4c55a47976cdc45d898428f1127aafd3ba753d69fa3f18cde4c2ff59636"
  },
  {
    "file": "exercise.mjs",
    "bytes": 1711,
    "sha256": "1dd851b1377b17f1dd4dcbadaa1c6918f7682583e7289a378b736c4ab2fff5cc"
  },
  {
    "file": "../corpus/views-vertical-cases.json",
    "bytes": 9713,
    "sha256": "baaea5114511836b93a86962ccd02592e69dc9ed17c9de6579f7674a4b940a7f"
  }
]

function viewsState(t, { failAt } = {}) {
  const scratch = mkdtempSync(resolve(tmpdir(), 'harborline-views-frozen-'))
  t.after(() => rmSync(scratch, { recursive: true, force: true }))
  const consumer = resolve(scratch, 'views-npm-consumer')
  const npmArtifacts = resolve(scratch, 'npm')
  mkdirSync(npmArtifacts)
  const bytes = manifests.map(archive)
  const paths = ['ui-react', 'contracts', 'rule-engine', 'rule-authoring'].map((name, index) => {
    const path = resolve(npmArtifacts, `harborline-software-${name}-unit.tgz`)
    writeFileSync(path, bytes[index])
    return path
  })
  for (const ignored of ['unrelated.tgz', ...['ui-react', 'contracts', 'rule-engine', 'rule-authoring'].map(name => `harborline-software-${name}-ignored.txt`)]) {
    writeFileSync(resolve(npmArtifacts, ignored), 'ignored by the real packed resolver')
  }
  const state = {
    scratch, consumer, npmArtifacts, paths, bytes, calls: [], copies: [], directories: [], lookups: [], helperCalls: [],
    failure: new Error('ordinary Views pnpm failure'),
    copyFailure: new Error('ordinary Views copy failure'),
    mkdirFailure: new Error('ordinary Views directory failure'),
    nextBoundaryReached: false,
  }
  state.run = (command, args, options) => {
    state.calls.push({ command, args, cwd: options.cwd })
    assert.equal(options.cwd, consumer, 'all three operations target the Views scratch consumer')
    if (state.calls.length === failAt) throw state.failure
    if (state.calls.length === 1) {
      state.initialInputs = viewsCopiedInputs.map(({ file }) => {
        const data = readFileSync(resolve(consumer, file))
        return { file, bytes: data.length, sha256: createHash('sha256').update(data).digest('hex') }
      })
    }
    if (state.calls.length === 2) {
      const derived = viewsCapturedSris.reduce((text, sri, index) => text.replace(sri,
        `sha512-${createHash('sha512').update(bytes[index]).digest('base64')}`), viewsCapturedLock)
      writeFileSync(resolve(consumer, 'pnpm-lock.yaml'), derived)
      writeFileSync(resolve(consumer, 'package.json'), viewsCapturedManifest)
    }
    return ''
  }
  return state
}

function runViewsRendererInstall(state, { failCopy, failMkdir } = {}) {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const body = source.slice(source.indexOf('function verifyViewsCapability()'), source.indexOf('function verifySchedulingCapability()'))
  const start = body.indexOf('  const corpusSource = ')
  const end = body.indexOf('  assertPackedInstall(')
  assert.ok(start > 0 && end > start, 'complete actual Views renderer setup/install boundary')
  assert.ok(body.slice(end).startsWith("  assertPackedInstall(rendererConsumer, ['ui-react', 'rule-authoring', 'rule-engine', 'contracts'], 'Views renderer')"))
  assert.ok(body.indexOf('proofLines(clientRun(rendererConsumer)') > end)
  assert.ok(body.indexOf('  const engineConsumer = ') > end)
  assert.doesNotMatch(body, /installPackedArtifacts\(|package-lock=false|run\('npm', \['install'/, 'no mutable Views install fallback')
  const resolverStart = source.indexOf('function packedArtifact(')
  const resolverEnd = source.indexOf('function installPackedArtifacts(', resolverStart)
  assert.ok(resolverStart > 0 && resolverEnd > resolverStart, 'real shared exactly-one packed artifact resolver')
  const packedArtifact = new Function('readdirSync', 'npmArtifacts', 'resolve', `${source.slice(resolverStart, resolverEnd)}\nreturn packedArtifact`)(
    readdirSync, state.npmArtifacts, resolve)
  const lookup = (prefix, label) => {
    state.lookups.push({ prefix, label })
    try {
      return packedArtifact(prefix, label)
    } catch (error) {
      state.selectionFailure = error
      throw error
    }
  }
  const copy = (from, to) => {
    state.copies.push({ from, to })
    if (state.copies.length === failCopy) throw state.copyFailure
    copyFileSync(from, to)
  }
  const mkdir = (path, options) => {
    state.directories.push({ path, options })
    if (state.directories.length === failMkdir) throw state.mkdirFailure
    mkdirSync(path, options)
  }
  const read = (path, encoding) => {
    const text = readFileSync(path, encoding)
    if (path === resolve(root, 'conformance/hlp.blocks.entity-views/views-vertical-cases.json')) {
      const corpus = JSON.parse(text)
      state.corpusShape = { anchors: corpus.anchors.length, pairs: corpus.pairs.length, routes: corpus.routes.length,
        total: corpus.anchors.length + corpus.pairs.length + corpus.routes.length }
    }
    return text
  }
  const install = (consumer, paths, run) => {
    state.helperCalls.push({ consumer, paths, run })
    installFrozenUiConsumer(consumer, paths, run)
  }
  const nextBoundary = Symbol('stop before installed assertion, client/proof and native work')
  const setup = new Function('root', 'fixtureRoot', 'resolve', 'readFileSync', 'mkdirSync', 'copyFileSync', 'installFrozenUiConsumer', 'packedArtifact', 'run', 'nextBoundary', `${body.slice(start, end)}\nthrow nextBoundary`)
  try {
    setup(root, state.scratch, resolve, read, mkdir, copy, install, lookup, state.run, nextBoundary)
  } catch (error) {
    if (error !== nextBoundary) throw error
    state.nextBoundaryReached = true
  }
}

test('the actual Views renderer copies five literal inputs and installs four selected artifacts with the admitted graph', t => {
  assert.equal(Buffer.byteLength(viewsCapturedLock), 8107)
  assert.equal(createHash('sha256').update(viewsCapturedLock).digest('hex'), 'a9afc069d4d4d5d123ab5c8657e2615088fc625403dfd17252bd523de7967779')
  assert.equal(Buffer.byteLength(viewsCapturedManifest), 864)
  assert.equal(createHash('sha256').update(viewsCapturedManifest).digest('hex'), '1ba463bccdd9c6604cdf70f52752b7fa5879ea3cb44730a10ea310ea74ee662a')
  const state = viewsState(t)
  runViewsRendererInstall(state)
  assert.equal(state.initialInputs.length, 5)
  assert.deepEqual(state.initialInputs, viewsCopiedInputs)
  assert.deepEqual(state.corpusShape, { anchors: 9, pairs: 4, routes: 20, total: 33 })
  assert.deepEqual(state.copies, [
    { from: resolve(root, 'tests/package-consumers/npm/package.json'), to: resolve(state.consumer, 'package.json') },
    { from: resolve(root, 'tests/package-consumers/npm/pnpm-lock.yaml'), to: resolve(state.consumer, 'pnpm-lock.yaml') },
    { from: resolve(root, 'tests/package-consumers/npm/pnpm-workspace.yaml'), to: resolve(state.consumer, 'pnpm-workspace.yaml') },
    { from: resolve(root, 'tests/package-consumers/views-npm/exercise.mjs'), to: resolve(state.consumer, 'exercise.mjs') },
    { from: resolve(root, 'conformance/hlp.blocks.entity-views/views-vertical-cases.json'), to: resolve(state.consumer, '../corpus/views-vertical-cases.json') },
  ])
  assert.deepEqual(state.directories, [
    { path: state.consumer, options: { recursive: true } },
    { path: resolve(state.consumer, '../corpus'), options: { recursive: true } },
  ])
  assert.deepEqual(state.lookups, [
    { prefix: 'harborline-software-ui-react-', label: 'Views renderer' },
    { prefix: 'harborline-software-contracts-', label: 'Views renderer' },
    { prefix: 'harborline-software-rule-engine-', label: 'Views renderer' },
    { prefix: 'harborline-software-rule-authoring-', label: 'Views renderer' },
  ])
  assert.deepEqual(state.helperCalls, [{ consumer: state.consumer, paths: state.paths, run: state.run }])
  assert.deepEqual(state.calls, [
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--ignore-scripts'], cwd: state.consumer },
    { command: 'pnpm', args: ['add', '--lockfile-only', '--offline', '--ignore-scripts', 'file:.packed/ui-react.tgz', 'file:.packed/contracts.tgz', 'file:.packed/rule-engine.tgz', 'file:.packed/rule-authoring.tgz'], cwd: state.consumer },
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], cwd: state.consumer },
  ])
  for (const [index, name] of ['ui-react', 'contracts', 'rule-engine', 'rule-authoring'].entries()) {
    assert.deepEqual(readFileSync(state.paths[index]), state.bytes[index])
    assert.deepEqual(readFileSync(resolve(state.consumer, `.packed/${name}.tgz`)), state.bytes[index])
  }
  for (const { file, bytes, sha256 } of viewsCopiedInputs.slice(2)) {
    const data = readFileSync(resolve(state.consumer, file))
    assert.equal(data.length, bytes)
    assert.equal(createHash('sha256').update(data).digest('hex'), sha256)
  }
  assert.equal(readFileSync(resolve(state.consumer, 'package.json'), 'utf8'), viewsCapturedManifest)
  assert.equal(state.nextBoundaryReached, true)
})

for (const failCopy of [1, 2, 3, 4, 5]) test(`Views copy operation ${failCopy} failure stops before selection or installation`, t => {
  const state = viewsState(t)
  assert.throws(() => runViewsRendererInstall(state, { failCopy }), error => error === state.copyFailure)
  assert.equal(state.copies.length, failCopy)
  assert.equal(state.directories.length, failCopy === 5 ? 2 : 1)
  assert.deepEqual(state.lookups, [])
  assert.deepEqual(state.helperCalls, [])
  assert.equal(state.calls.length, 0)
  assert.equal(state.nextBoundaryReached, false)
})

for (const failMkdir of [1, 2]) test(`Views directory operation ${failMkdir} failure stops before selection or installation`, t => {
  const state = viewsState(t)
  assert.throws(() => runViewsRendererInstall(state, { failMkdir }), error => error === state.mkdirFailure)
  assert.equal(state.directories.length, failMkdir)
  assert.equal(state.copies.length, failMkdir === 1 ? 0 : 4)
  assert.deepEqual(state.lookups, [])
  assert.deepEqual(state.helperCalls, [])
  assert.equal(state.calls.length, 0)
  assert.equal(state.nextBoundaryReached, false)
})

for (const [index, name] of ['ui-react', 'contracts', 'rule-engine', 'rule-authoring'].entries()) {
  for (const matches of [0, 2]) test(`Views real resolver refuses ${matches} matching ${name} archives before installation`, t => {
    const state = viewsState(t)
    if (matches === 0) rmSync(state.paths[index])
    else writeFileSync(resolve(state.npmArtifacts, `harborline-software-${name}-duplicate.tgz`), state.bytes[index])
    assert.throws(() => runViewsRendererInstall(state), error => {
      assert.equal(error, state.selectionFailure)
      assert.match(error.message, new RegExp(`Views renderer requires exactly one packed harborline-software-${name}-`))
      return true
    })
    assert.equal(state.copies.length, 5)
    assert.equal(state.directories.length, 2)
    assert.equal(state.lookups.length, index + 1)
    assert.deepEqual(state.helperCalls, [])
    assert.equal(state.calls.length, 0)
    assert.equal(state.nextBoundaryReached, false)
  })
}

for (const failAt of [1, 2, 3]) test(`Views frozen command ${failAt} failure propagates before the next boundary`, t => {
  const state = viewsState(t, { failAt })
  assert.throws(() => runViewsRendererInstall(state), error => error === state.failure)
  assert.equal(state.calls.length, failAt)
  assert.equal(state.helperCalls.length, 1)
  assert.equal(state.copies.length, 5)
  assert.equal(state.directories.length, 2)
  assert.equal(state.lookups.length, 4)
  assert.equal(state.nextBoundaryReached, false)
  if (failAt === 1) assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
})

const aggregatesCopiedInputs = [
  {
    "file": "package.json",
    "bytes": 576,
    "sha256": "1ed842c0c5da0e6c0906eb245c5aea075de6fafd45be722b0bb1a774c3fd139d"
  },
  {
    "file": "pnpm-lock.yaml",
    "bytes": 3390,
    "sha256": "2a9aa0df9998309ba77aab5f9b9e7d7a759f2812d7d8e04158c2a08708fee51e"
  },
  {
    "file": "pnpm-workspace.yaml",
    "bytes": 60,
    "sha256": "c8b2f4c55a47976cdc45d898428f1127aafd3ba753d69fa3f18cde4c2ff59636"
  },
  {
    "file": "exercise.mjs",
    "bytes": 3114,
    "sha256": "e34eebd16d943dd9d735d9d66cc4d8cc98c1ab3d683a9116412017109d45eba9"
  },
  {
    "file": "../corpus/aggregates-vertical-cases.json",
    "bytes": 5082,
    "sha256": "b1e543380809d40f02b7ab20a8cc2e29a8c9c41ea9a0cd151b221b0d3d98d081"
  }
]

function aggregatesState(t, { failAt } = {}) {
  const scratch = mkdtempSync(resolve(tmpdir(), 'harborline-aggregates-frozen-'))
  t.after(() => rmSync(scratch, { recursive: true, force: true }))
  const consumer = resolve(scratch, 'aggregates-capability-npm-consumer')
  const npmArtifacts = resolve(scratch, 'npm')
  mkdirSync(npmArtifacts)
  const bytes = manifests.map(archive)
  const paths = ['ui-react', 'contracts', 'rule-engine', 'rule-authoring'].map((name, index) => {
    const path = resolve(npmArtifacts, `harborline-software-${name}-unit.tgz`)
    writeFileSync(path, bytes[index])
    return path
  })
  for (const ignored of ['unrelated.tgz', ...['ui-react', 'contracts', 'rule-engine', 'rule-authoring'].map(name => `harborline-software-${name}-ignored.txt`)]) {
    writeFileSync(resolve(npmArtifacts, ignored), 'ignored by the real packed resolver')
  }
  const state = {
    scratch, consumer, npmArtifacts, paths, bytes, calls: [], copies: [], directories: [], lookups: [], helperCalls: [],
    failure: new Error('ordinary Aggregates pnpm failure'),
    copyFailure: new Error('ordinary Aggregates copy failure'),
    mkdirFailure: new Error('ordinary Aggregates directory failure'),
    nextBoundaryReached: false,
  }
  state.run = (command, args, options) => {
    state.calls.push({ command, args, cwd: options.cwd })
    assert.equal(options.cwd, consumer, 'all three operations target the Aggregates scratch consumer')
    if (state.calls.length === failAt) throw state.failure
    if (state.calls.length === 1) {
      state.initialInputs = aggregatesCopiedInputs.map(({ file }) => {
        const data = readFileSync(resolve(consumer, file))
        return { file, bytes: data.length, sha256: createHash('sha256').update(data).digest('hex') }
      })
    }
    if (state.calls.length === 2) {
      const derived = viewsCapturedSris.reduce((text, sri, index) => text.replace(sri,
        `sha512-${createHash('sha512').update(bytes[index]).digest('base64')}`), viewsCapturedLock)
      writeFileSync(resolve(consumer, 'pnpm-lock.yaml'), derived)
      writeFileSync(resolve(consumer, 'package.json'), viewsCapturedManifest)
    }
    return ''
  }
  return state
}

function runAggregatesRendererInstall(state, { failCopy, failMkdir } = {}) {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const body = source.slice(source.indexOf('function verifyAggregatesCapability()'), source.indexOf('function stageCheckedTarball('))
  const guardStart = body.indexOf('  const corpusSource = ')
  const engineStart = body.indexOf('  // NuGet lane runs FIRST')
  const rendererStart = body.indexOf('  const rendererConsumer = ')
  const end = body.indexOf('  assertPackedInstall(')
  assert.ok(guardStart > 0 && engineStart > guardStart && rendererStart > engineStart && end > rendererStart,
    'separate actual Aggregates guards and renderer setup around the preserved engine-first block')
  const guards = body.slice(guardStart, engineStart)
  const renderer = body.slice(rendererStart, end)
  assert.equal(Buffer.byteLength(guards), 908)
  assert.equal(createHash('sha256').update(guards).digest('hex'), 'e2240922fe8beca78c1a8fd016fc1d285b328bc977bbb33ac87092437baff0c9')
  assert.equal(createHash('sha256').update(body.slice(engineStart, rendererStart)).digest('hex'), 'fed64890d196ec339e790b423c397532e900596260a9e68fe59d6661d0e69f5e')
  assert.equal(createHash('sha256').update(body.slice(end)).digest('hex'), 'ed8aa8e025031d6aa33ef99cd6f17ec15bb974da09bd5dbe51c0f5d9d51496a2')
  assert.doesNotMatch(guards + renderer, /engineTimeline|engineConsumer|runNugetConsumer|proofLines|clientRun/,
    'unit slices supply no engine timeline and invoke no native or client proof')
  assert.ok(body.slice(end).startsWith("  assertPackedInstall(rendererConsumer, ['ui-react', 'rule-authoring', 'rule-engine', 'contracts'], 'Aggregates renderer')"))
  assert.doesNotMatch(body, /installPackedArtifacts\(|package-lock=false|run\('npm', \['install'/, 'no mutable Aggregates install fallback')
  const resolverStart = source.indexOf('function packedArtifact(')
  const resolverEnd = source.indexOf('function installPackedArtifacts(', resolverStart)
  assert.ok(resolverStart > 0 && resolverEnd > resolverStart, 'real shared exactly-one packed artifact resolver')
  const packedArtifact = new Function('readdirSync', 'npmArtifacts', 'resolve', `${source.slice(resolverStart, resolverEnd)}\nreturn packedArtifact`)(
    readdirSync, state.npmArtifacts, resolve)
  const lookup = (prefix, label) => {
    state.lookups.push({ prefix, label })
    try {
      return packedArtifact(prefix, label)
    } catch (error) {
      state.selectionFailure = error
      throw error
    }
  }
  const copy = (from, to) => {
    state.copies.push({ from, to })
    if (state.copies.length === failCopy) throw state.copyFailure
    copyFileSync(from, to)
  }
  const mkdir = (path, options) => {
    state.directories.push({ path, options })
    if (state.directories.length === failMkdir) throw state.mkdirFailure
    mkdirSync(path, options)
  }
  const read = (path, encoding) => {
    const text = readFileSync(path, encoding)
    if (path === resolve(root, 'conformance/hlp.blocks.activity-timeline/aggregates-vertical-cases.json')) {
      const corpus = JSON.parse(text)
      state.corpusShape = {
        ported: corpus.portedCases.length, authored: corpus.authoredCases.length,
        carried: corpus.carriedUiCases.length, pairs: corpus.crossLanePairs.length,
        authoredObligations: corpus.authoredCases.every(row => row.authored === true && Boolean(row.obligationRef)),
        carriedConformance: corpus.carriedUiCases.every(row => row.carried === true && Boolean(row.conformanceId)),
      }
    }
    return text
  }
  const install = (consumer, paths, run) => {
    state.helperCalls.push({ consumer, paths, run })
    installFrozenUiConsumer(consumer, paths, run)
  }
  const nextBoundary = Symbol('stop before installed assertion, client/proof and timeline comparison')
  const setup = new Function('root', 'fixtureRoot', 'resolve', 'readFileSync', 'mkdirSync', 'copyFileSync', 'installFrozenUiConsumer', 'packedArtifact', 'run', 'nextBoundary', `${guards}${renderer}\nthrow nextBoundary`)
  try {
    setup(root, state.scratch, resolve, read, mkdir, copy, install, lookup, state.run, nextBoundary)
  } catch (error) {
    if (error !== nextBoundary) throw error
    state.nextBoundaryReached = true
  }
}

test('the actual Aggregates split caller copies five literal inputs and installs four selected artifacts without engine proof', t => {
  assert.equal(Buffer.byteLength(viewsCapturedLock), 8107)
  assert.equal(createHash('sha256').update(viewsCapturedLock).digest('hex'), 'a9afc069d4d4d5d123ab5c8657e2615088fc625403dfd17252bd523de7967779')
  assert.equal(Buffer.byteLength(viewsCapturedManifest), 864)
  assert.equal(createHash('sha256').update(viewsCapturedManifest).digest('hex'), '1ba463bccdd9c6604cdf70f52752b7fa5879ea3cb44730a10ea310ea74ee662a')
  const state = aggregatesState(t)
  runAggregatesRendererInstall(state)
  assert.equal(state.initialInputs.length, 5)
  assert.deepEqual(state.initialInputs, aggregatesCopiedInputs)
  assert.deepEqual(state.corpusShape, { ported: 7, authored: 4, carried: 20, pairs: 4, authoredObligations: true, carriedConformance: true })
  assert.deepEqual(state.copies, [
    { from: resolve(root, 'tests/package-consumers/npm/package.json'), to: resolve(state.consumer, 'package.json') },
    { from: resolve(root, 'tests/package-consumers/npm/pnpm-lock.yaml'), to: resolve(state.consumer, 'pnpm-lock.yaml') },
    { from: resolve(root, 'tests/package-consumers/npm/pnpm-workspace.yaml'), to: resolve(state.consumer, 'pnpm-workspace.yaml') },
    { from: resolve(root, 'tests/package-consumers/aggregates-npm/exercise.mjs'), to: resolve(state.consumer, 'exercise.mjs') },
    { from: resolve(root, 'conformance/hlp.blocks.activity-timeline/aggregates-vertical-cases.json'), to: resolve(state.consumer, '../corpus/aggregates-vertical-cases.json') },
  ])
  assert.deepEqual(state.directories, [
    { path: state.consumer, options: { recursive: true } },
    { path: resolve(state.consumer, '../corpus'), options: { recursive: true } },
  ])
  assert.deepEqual(state.lookups, [
    { prefix: 'harborline-software-ui-react-', label: 'Aggregates renderer' },
    { prefix: 'harborline-software-contracts-', label: 'Aggregates renderer' },
    { prefix: 'harborline-software-rule-engine-', label: 'Aggregates renderer' },
    { prefix: 'harborline-software-rule-authoring-', label: 'Aggregates renderer' },
  ])
  assert.deepEqual(state.helperCalls, [{ consumer: state.consumer, paths: state.paths, run: state.run }])
  assert.deepEqual(state.calls, [
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--ignore-scripts'], cwd: state.consumer },
    { command: 'pnpm', args: ['add', '--lockfile-only', '--offline', '--ignore-scripts', 'file:.packed/ui-react.tgz', 'file:.packed/contracts.tgz', 'file:.packed/rule-engine.tgz', 'file:.packed/rule-authoring.tgz'], cwd: state.consumer },
    { command: 'pnpm', args: ['install', '--frozen-lockfile', '--offline', '--ignore-scripts'], cwd: state.consumer },
  ])
  for (const [index, name] of ['ui-react', 'contracts', 'rule-engine', 'rule-authoring'].entries()) {
    assert.deepEqual(readFileSync(state.paths[index]), state.bytes[index])
    assert.deepEqual(readFileSync(resolve(state.consumer, `.packed/${name}.tgz`)), state.bytes[index])
  }
  for (const { file, bytes, sha256 } of aggregatesCopiedInputs.slice(2)) {
    const data = readFileSync(resolve(state.consumer, file))
    assert.equal(data.length, bytes)
    assert.equal(createHash('sha256').update(data).digest('hex'), sha256)
  }
  assert.equal(readFileSync(resolve(state.consumer, 'package.json'), 'utf8'), viewsCapturedManifest)
  assert.equal(state.nextBoundaryReached, true)
})

for (const failCopy of [1, 2, 3, 4, 5]) test(`Aggregates copy operation ${failCopy} failure stops before selection or installation`, t => {
  const state = aggregatesState(t)
  assert.throws(() => runAggregatesRendererInstall(state, { failCopy }), error => error === state.copyFailure)
  assert.equal(state.copies.length, failCopy)
  assert.equal(state.directories.length, failCopy === 5 ? 2 : 1)
  assert.deepEqual(state.lookups, [])
  assert.deepEqual(state.helperCalls, [])
  assert.equal(state.calls.length, 0)
  assert.equal(state.nextBoundaryReached, false)
})

for (const failMkdir of [1, 2]) test(`Aggregates directory operation ${failMkdir} failure stops before selection or installation`, t => {
  const state = aggregatesState(t)
  assert.throws(() => runAggregatesRendererInstall(state, { failMkdir }), error => error === state.mkdirFailure)
  assert.equal(state.directories.length, failMkdir)
  assert.equal(state.copies.length, failMkdir === 1 ? 0 : 4)
  assert.deepEqual(state.lookups, [])
  assert.deepEqual(state.helperCalls, [])
  assert.equal(state.calls.length, 0)
  assert.equal(state.nextBoundaryReached, false)
})

for (const [index, name] of ['ui-react', 'contracts', 'rule-engine', 'rule-authoring'].entries()) {
  for (const matches of [0, 2]) test(`Aggregates real resolver refuses ${matches} matching ${name} archives before installation`, t => {
    const state = aggregatesState(t)
    if (matches === 0) rmSync(state.paths[index])
    else writeFileSync(resolve(state.npmArtifacts, `harborline-software-${name}-duplicate.tgz`), state.bytes[index])
    assert.throws(() => runAggregatesRendererInstall(state), error => {
      assert.equal(error, state.selectionFailure)
      assert.match(error.message, new RegExp(`Aggregates renderer requires exactly one packed harborline-software-${name}-`))
      return true
    })
    assert.equal(state.copies.length, 5)
    assert.equal(state.directories.length, 2)
    assert.equal(state.lookups.length, index + 1)
    assert.deepEqual(state.helperCalls, [])
    assert.equal(state.calls.length, 0)
    assert.equal(state.nextBoundaryReached, false)
  })
}

for (const failAt of [1, 2, 3]) test(`Aggregates frozen command ${failAt} failure propagates before the next boundary`, t => {
  const state = aggregatesState(t, { failAt })
  assert.throws(() => runAggregatesRendererInstall(state), error => error === state.failure)
  assert.equal(state.calls.length, failAt)
  assert.equal(state.helperCalls.length, 1)
  assert.equal(state.copies.length, 5)
  assert.equal(state.directories.length, 2)
  assert.equal(state.lookups.length, 4)
  assert.equal(state.nextBoundaryReached, false)
  if (failAt === 1) assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
})


test('the UI helper installs the selected four-artifact graph with the required Contracts peer', t => {
  const state = stage(t)
  installFrozenUiConsumer(state.consumer, state.paths, state.run)
  assert.equal(state.calls.length, 3)
  assert.equal(readFileSync(resolve(state.consumer, 'pnpm-lock.yaml'), 'utf8'), derivedLock(state.bytes))
})

test('the Contracts helper retains the admitted singleton graph', t => {
  const state = contractsState(t)
  installFrozenContractsConsumer(state.consumer, state.artifact, state.run)
  assert.equal(state.calls.length, 3)
})

for (const [label, field, value, expected] of [
  ['missing', 'peerDependencies', undefined, /exactly the selected Contracts peer/],
  ['extra', 'peerDependencies', { '@harborline-software/contracts': '0.0.0-alpha.0', other: '1.0.0' }, /exactly the selected Contracts peer/],
  ['mismatched', 'peerDependencies', { '@harborline-software/contracts': '0.0.0-alpha.1' }, /exactly the selected Contracts peer/],
  ['range', 'peerDependencies', { '@harborline-software/contracts': '^0.0.0' }, /exactly the selected Contracts peer/],
  ['optional', 'peerDependenciesMeta', { '@harborline-software/contracts': { optional: true } }, /Contracts peer must be required/],
]) {
  test(`Rule Runtime rejects a ${label} Contracts peer before derivation`, t => {
    const manifest = { ...manifests[2], [field]: value }
    const state = ruleRuntimeState(t, { manifest })
    assert.throws(() => runRuleRuntimeCaller(state), expected)
    assert.equal(state.calls.length, 1)
    assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
  })
  test(`Rule Authoring rejects a ${label} runtime Contracts peer before derivation`, t => {
    const packedManifests = structuredClone(ruleAuthoringTestManifests)
    packedManifests[1][field] = value
    const state = ruleAuthoringState(t, { packedManifests })
    assert.throws(() => runRuleAuthoringCaller(state), expected)
    assert.equal(state.calls.length, 1)
    assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
  })
  test(`UI rejects a ${label} runtime Contracts peer before derivation`, t => {
    const state = stage(t)
    writeFileSync(state.paths[2], archive({ ...manifests[2], [field]: value }))
    assert.throws(() => installFrozenUiConsumer(state.consumer, state.paths, state.run), expected)
    assert.equal(state.calls.length, 1)
  })
}

for (const number of [2, 3]) for (const target of ['producer', 'staged']) {
  test(`Rule Runtime command ${number} preserves the selected Contracts ${target} bytes`, t => {
    const state = ruleRuntimeState(t, { afterCall: (count, { consumer, contractsArtifact }) => {
      if (count === number) writeFileSync(target === 'producer' ? contractsArtifact : resolve(consumer, '.packed/contracts.tgz'), 'changed')
    } })
    assert.throws(() => runRuleRuntimeCaller(state), /Rule Runtime (?:producer|staged) tarball changed/)
    assert.equal(state.calls.length, number)
  })
}

for (const [label, contractsManifest, expected] of [
  ['identity', { name: '@harborline-software/other', version: '0.0.0-alpha.0' }, /identity mismatch/],
  ['version', { ...manifests[1], version: 'unsafe\nversion' }, /unsupported Rule Runtime package version/],
  ['selected version', { ...manifests[1], version: '0.0.0-alpha.1' }, /exactly the selected Contracts peer/],
  ...['dependencies', 'optionalDependencies', 'peerDependencies', 'peerDependenciesMeta'].map(field => [field, { ...manifests[1], [field]: { other: '1.0.0' } }, /must be empty/]),
]) test(`Rule Runtime refuses Contracts archive ${label} before derivation`, t => {
  const state = ruleRuntimeState(t, { contractsManifest })
  assert.throws(() => runRuleRuntimeCaller(state), expected)
  assert.equal(state.calls.length, 1)
  assert.equal(existsSync(resolve(state.consumer, '.packed')), false)
})
