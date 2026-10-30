import assert from 'node:assert/strict'
import { existsSync, readFileSync, readdirSync, mkdtempSync, writeFileSync, rmSync, mkdirSync, cpSync, copyFileSync } from 'node:fs'
import { createHash } from 'node:crypto'
import { runInNewContext } from 'node:vm'
import { tmpdir } from 'node:os'
import { resolve } from 'node:path'
import { test } from 'node:test'

const root = resolve(import.meta.dirname, '../..')

test('app-shell consumer installs with frozen pnpm and never repairs its lock', () => {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
    .split('function verifyAppShellCapability() {')[1].split('\ntry {')[0]

  assert.match(source, /run\('pnpm', \['install', '--frozen-lockfile', '--ignore-scripts'\], \{ cwd: npmConsumer \}\)/)
  assert.doesNotMatch(source, /run\('npm'|--no-frozen-lockfile|--lockfile-only|--package-lock=false/)
  assert.match(source, /cpSync\(resolve\(root, 'tests\/package-consumers\/appshell-npm'\), npmConsumer, \{ recursive: true \}\)/)
  assert.doesNotMatch(source, /writeFileSync\([^\n]*pnpm-lock\.yaml/)
  assert.ok(existsSync(resolve(root, 'tests/package-consumers/appshell-npm/pnpm-lock.yaml')), 'app-shell frozen install requires a committed lock')
})

// Literal oracle: the reviewed T089 provenance, two-file local graph and empty consumer manifest.
const approvedInputs = {
  files: readdirSync(resolve(root, 'tests/package-consumers/appshell-npm')),
  provenance: readFileSync(resolve(root, 'tooling/appshell-feed-provenance.json')),
  lock: readFileSync(resolve(root, 'tests/package-consumers/appshell-npm/pnpm-lock.yaml')),
  manifest: readFileSync(resolve(root, 'tests/package-consumers/appshell-npm/package.json')),
}

function localFeedSettings() {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const start = source.indexOf('function appShellLocalFeedSettings(')
  assert.ok(start >= 0, 'local-only admission must exist before the age exception')
  const end = source.indexOf('\nfunction verifyAppShellCapability()', start)
  return runInNewContext(`${source.slice(start, end)}; appShellLocalFeedSettings`, { createHash })
}

test('T1035 admits exactly the approved local graph and two age selectors', () => {
  const settings = localFeedSettings()(approvedInputs)
  assert.deepEqual(JSON.parse(JSON.stringify(settings)), {
    overrides: { '@harborline-software/api-contracts': 'file:./feed/harborline-software-api-contracts-0.1.0-alpha.1.tgz' },
    minimumReleaseAgeExclude: [
      '@harborline-software/api-contracts@0.1.0-alpha.1',
      '@harborline-software/capability-host@0.0.0',
    ],
  })
})

test('T1035 refuses changed provenance, missing or stale lock, and changed manifest', () => {
  const settings = localFeedSettings()
  for (const key of ['provenance', 'lock', 'manifest']) {
    assert.throws(() => settings({ ...approvedInputs, [key]: Buffer.concat([approvedInputs[key], Buffer.from(' ')]) }), /T1035 local-only approval/)
    assert.throws(() => settings({ ...approvedInputs, [key]: undefined }))
  }
})

test('T1035 refuses scoped lookalikes, registry substitutions and extra dependency lanes', () => {
  const settings = localFeedSettings()
  for (const dependencies of [
    { '@harborline-software/lookalike': '1.0.0' },
    { '@harborline-software/capability-host': 'npm:@lookalike/capability-host@0.0.0' },
  ]) {
    const manifest = Buffer.from(JSON.stringify({ ...JSON.parse(approvedInputs.manifest), dependencies }))
    assert.throws(() => settings({ ...approvedInputs, manifest }), /T1035 local-only approval/)
  }
  for (const key of ['devDependencies', 'optionalDependencies', 'peerDependencies', 'pnpm']) {
    const manifest = Buffer.from(JSON.stringify({ ...JSON.parse(approvedInputs.manifest), [key]: { '@harborline-software/lookalike': '*' } }))
    assert.throws(() => settings({ ...approvedInputs, manifest }), /T1035 local-only approval/)
  }
  for (const extra of ['.npmrc', 'pnpm-workspace.yaml', '.pnpmfile.cjs', 'node_modules']) {
    assert.throws(() => settings({ ...approvedInputs, files: [...approvedInputs.files, extra] }), /T1035 local-only approval/)
  }
  const lock = Buffer.from(approvedInputs.lock.toString().replace('file:feed/', 'https://registry.npmjs.org/'))
  assert.throws(() => settings({ ...approvedInputs, lock }), /T1035 local-only approval/)
})

test('T1035 guards the closed graph before staging and exceptions, then runs frozen', () => {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
    .split('function verifyAppShellCapability() {')[1].split('\ntry {')[0]
  const guard = source.indexOf('appShellLocalFeedSettings(')
  assert.ok(guard >= 0 && guard < source.indexOf('stageCheckedTarball('))
  assert.match(source, /JSON\.parse\(npmManifestBytes\)/)
  assert.ok(source.indexOf('stageCheckedTarball(') < source.indexOf("writeFileSync(resolve(npmConsumer, 'pnpm-workspace.yaml')"))
  assert.match(source, /stageCheckedTarball\(process\.env\[artifact\.env\], resolve\(feed, artifact\.tarball\), artifact\.sha256\)/)
  assert.match(source, /writeFileSync\(resolve\(npmConsumer, 'pnpm-workspace\.yaml'\), `\$\{JSON\.stringify\(localFeedSettings/)
  assert.doesNotMatch(source, /trustPolicyExclude|trustLockfile|minimumReleaseAgeStrict|registry\.npmjs/)
})

test('T1035 staging refuses absent and mismatched local files instead of falling back', t => {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const start = source.indexOf('function stageCheckedTarball(')
  const end = source.indexOf('\nfunction walkFiles(', start)
  const stage = runInNewContext(`${source.slice(start, end)}; stageCheckedTarball`, { createHash, readFileSync, writeFileSync })
  const dir = mkdtempSync(resolve(tmpdir(), 't1035-local-stage-'))
  t.after(() => rmSync(dir, { recursive: true, force: true }))
  const input = resolve(dir, 'local.tgz')
  const output = resolve(dir, 'staged.tgz')
  // SHA-256 of the independent literal fixture, not a production-derived oracle.
  const expected = '4717c5ea3c7150f830a7856eb5bd7ca6f6453240a22601eae4eb9df7260d4ccb'
  assert.throws(() => stage(undefined, output, expected), /environment tarball path missing/)
  assert.throws(() => stage(input, output, expected), /ENOENT/)
  writeFileSync(input, 'same-name lookalike')
  assert.throws(() => stage(input, output, expected), /SHA-256 mismatch/)
  assert.equal(existsSync(output), false, 'no alternate source or staged output after refusal')
  writeFileSync(input, 'T1035 approved local fixture\n')
  stage(input, output, expected)
  assert.equal(readFileSync(output, 'utf8'), 'T1035 approved local fixture\n')
})

test('T1035 actual install caller refuses before exceptions or manager calls', t => {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const callerStart = source.indexOf("  const npmConsumer = resolve(fixtureRoot, 'appshell-capability-npm-consumer')")
  const callerEnd = source.indexOf('  const npmOutput = ', callerStart)
  assert.ok(callerStart >= 0 && callerEnd > callerStart)
  const stageStart = source.indexOf('function stageCheckedTarball(')
  const stageEnd = source.indexOf('\nfunction walkFiles(', stageStart)
  const stage = runInNewContext(`${source.slice(stageStart, stageEnd)}; stageCheckedTarball`, { createHash, readFileSync, writeFileSync })
  const dir = mkdtempSync(resolve(tmpdir(), 't1035-caller-refusal-'))
  t.after(() => rmSync(dir, { recursive: true, force: true }))
  for (const fault of ['provenance', 'lock', 'missing-lock', 'lookalike', 'configuration', 'missing-local', 'mismatched-local']) {
    const caseRoot = resolve(dir, fault)
    const fixture = resolve(caseRoot, 'tests/package-consumers/appshell-npm')
    const provenance = resolve(caseRoot, 'tooling/appshell-feed-provenance.json')
    mkdirSync(resolve(caseRoot, 'tooling'), { recursive: true })
    cpSync(resolve(root, 'tests/package-consumers/appshell-npm'), fixture, { recursive: true })
    writeFileSync(provenance, approvedInputs.provenance)
    const contractSource = resolve(caseRoot, 'contract.json')
    writeFileSync(contractSource, '{}')
    if (fault === 'provenance') writeFileSync(provenance, Buffer.concat([approvedInputs.provenance, Buffer.from(' ')]))
    if (fault === 'lock') writeFileSync(resolve(fixture, 'pnpm-lock.yaml'), Buffer.concat([approvedInputs.lock, Buffer.from(' ')]))
    if (fault === 'missing-lock') rmSync(resolve(fixture, 'pnpm-lock.yaml'))
    if (fault === 'lookalike') writeFileSync(resolve(fixture, 'package.json'), JSON.stringify({ ...JSON.parse(approvedInputs.manifest), dependencies: { '@harborline-software/lookalike': '*' } }))
    if (fault === 'configuration') writeFileSync(resolve(fixture, '.npmrc'), '@harborline-software:registry=https://registry.npmjs.org/')
    const env = {}
    if (fault === 'mismatched-local') {
      env.HARBORLINE_CONTRACTS_TARBALL = resolve(caseRoot, 'lookalike.tgz')
      writeFileSync(env.HARBORLINE_CONTRACTS_TARBALL, 'same-name substituted local input')
    }
    const counts = { stage: 0, exceptionWrites: 0, managerCalls: 0 }
    const caller = runInNewContext(`(function () {${source.slice(callerStart, callerEnd)}})`, {
      root: caseRoot, fixtureRoot: caseRoot, contractSource, resolve, cpSync, copyFileSync, mkdirSync, readFileSync, readdirSync,
      process: { env }, appShellLocalFeedSettings: localFeedSettings(),
      stageCheckedTarball: (...args) => { counts.stage++; return stage(...args) },
      writeFileSync: (path, ...args) => {
        if (path.endsWith('pnpm-workspace.yaml')) counts.exceptionWrites++
        return writeFileSync(path, ...args)
      },
      run: () => { counts.managerCalls++; throw new Error('unexpected package-manager execution') },
    })
    assert.throws(caller, fault === 'missing-lock' ? /ENOENT/ : fault === 'missing-local' ? /environment tarball path missing/ : fault === 'mismatched-local' ? /SHA-256 mismatch/ : /T1035 local-only approval/, fault)
    assert.equal(counts.exceptionWrites, 0, `${fault}: exception not written`)
    assert.equal(counts.managerCalls, 0, `${fault}: package manager not invoked`)
    assert.equal(counts.stage, fault.endsWith('-local') ? 1 : 0, `${fault}: input guard precedes staging`)
  }
})
