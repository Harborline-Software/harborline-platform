// Separate signing job (owner rulings 2026-11-03: "Separate signing job", and for Platform "Keep push to main"; the
// same design as harborline-api packages.yml at b401a2c). publish-libraries runs tooling/verify-attestations.sh before
// it pushes anything. These cases run that script against a stand-in `gh` that enforces what the real
// `gh attestation verify` does: the file's own SHA-256 must match an attestation subject whose signer workflow, source
// commit and runner kind match the flags given. A real signed bundle cannot be minted in a unit test (it needs a
// Sigstore certificate issued to a GitHub-hosted run's OIDC token), so the stand-in isolates each way verification must
// fail. The workflow cases pin who may sign, when, and that publish-libraries verifies before pushing.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import test from 'node:test'

const root = path.resolve(import.meta.dirname, '../..')
const script = path.join(root, 'tooling/verify-attestations.sh')
const REPO = 'Harborline-Software/harborline-platform'
const SHA = 'a'.repeat(40)
const SIGNER = `${REPO}/.github/workflows/validate.yml`
// On Windows a bare `bash` may resolve to the WSL launcher, which cannot see these paths. CI runs on Linux.
const skip = process.platform === 'win32' ? 'runs on Linux, where bash is the checked-out shell' : false

const STAND_IN = `#!/usr/bin/env node
const fs = require('fs'), crypto = require('crypto')
const args = process.argv.slice(2)
fs.appendFileSync(process.env.FAKE_LOG, JSON.stringify(args) + '\\n')
if (args[0] !== 'attestation' || args[1] !== 'verify') process.exit(64)
const flag = name => { const i = args.indexOf(name); return i < 0 ? undefined : args[i + 1] }
const deny = args.includes('--deny-self-hosted-runners')
const digest = crypto.createHash('sha256').update(fs.readFileSync(args[2])).digest('hex')
const ok = JSON.parse(fs.readFileSync(process.env.FAKE_ATTESTATIONS, 'utf8')).some(a =>
  a.digest === digest && a.repo === flag('--repo') && a.signer === flag('--signer-workflow') &&
  a.source === flag('--source-digest') && (!deny || a.hosted))
if (!ok) { console.error('no matching attestation'); process.exit(1) }
`

const files = {
  'Harborline.Foundation.0.1.0-preview.7.nupkg': 'foundation bytes',
  'Harborline.Kernel.Core.0.1.0-preview.7.nupkg': 'kernel bytes',
  'manifest.json': '{"schema":"harborline-platform/library-manifest/1"}\n',
  'consumer-proof.json': '{"schema":"harborline-platform/library-proof/1"}\n',
}
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex')
const attestation = (name, overrides = {}) => ({ digest: sha256(files[name]), repo: REPO, signer: SIGNER, source: SHA, hosted: true, ...overrides })
const all = () => Object.keys(files).map(name => attestation(name))

function setup(t, { attestations, write = files }) {
  const dir = mkdtempSync(path.join(tmpdir(), 'verify-attestations-'))
  t.after(() => rmSync(dir, { recursive: true, force: true }))
  const bin = path.join(dir, 'bin'); const packages = path.join(dir, 'packages')
  mkdirSync(bin); mkdirSync(packages)
  writeFileSync(path.join(bin, 'gh'), STAND_IN); chmodSync(path.join(bin, 'gh'), 0o755)
  for (const [name, bytes] of Object.entries(write)) writeFileSync(path.join(packages, name), bytes)
  writeFileSync(path.join(dir, 'attestations.json'), JSON.stringify(attestations))
  const log = path.join(dir, 'gh.log')
  return () => {
    writeFileSync(log, '')
    const result = spawnSync('bash', [script, packages], { encoding: 'utf8', env: { ...process.env,
      PATH: `${bin}${path.delimiter}${process.env.PATH}`, GITHUB_REPOSITORY: REPO, GITHUB_SHA: SHA,
      FAKE_ATTESTATIONS: path.join(dir, 'attestations.json'), FAKE_LOG: log } })
    return { ...result, calls: readFileSync(log, 'utf8').split('\n').filter(Boolean).map(line => JSON.parse(line)) }
  }
}

test('every library, the manifest and the consumer proof verify with the exact signer, commit and hosted-runner checks', { skip }, t => {
  const result = setup(t, { attestations: all() })()
  assert.equal(result.status, 0, result.stderr)
  assert.equal(result.calls.length, 4, 'one verification per file')
  for (const call of result.calls) {
    assert.deepEqual(call.slice(3), ['--repo', REPO, '--signer-workflow', SIGNER, '--source-digest', SHA, '--deny-self-hosted-runners'])
  }
  assert.deepEqual(result.calls.map(call => path.basename(call[2])).sort(), Object.keys(files).sort())
})

test('a file with no attestation stops publication', { skip }, t => {
  const missing = 'Harborline.Kernel.Core.0.1.0-preview.7.nupkg'
  const result = setup(t, { attestations: all().filter(a => a.digest !== sha256(files[missing])) })()
  assert.notEqual(result.status, 0)
  assert.match(result.stderr, /Harborline\.Kernel\.Core\.0\.1\.0-preview\.7\.nupkg has no attestation/)
})

test('an attestation signed by another workflow stops publication', { skip }, t => {
  const result = setup(t, { attestations: Object.keys(files).map(name => attestation(name,
    name === 'manifest.json' ? { signer: `${REPO}/.github/workflows/release-platform.yml` } : {})) })()
  assert.notEqual(result.status, 0)
  assert.match(result.stderr, /manifest\.json has no attestation/)
})

test('changed bytes match no attestation and stop publication', { skip }, t => {
  const result = setup(t, { attestations: all(), write: { ...files, 'consumer-proof.json': '{"tampered":true}\n' } })()
  assert.notEqual(result.status, 0)
  assert.match(result.stderr, /consumer-proof\.json has no attestation/)
})

test('an attestation for another commit or from a self-hosted runner stops publication', { skip }, t => {
  for (const overrides of [{ source: 'b'.repeat(40) }, { hosted: false }]) {
    const run = setup(t, { attestations: Object.keys(files).map(name => attestation(name, name.endsWith('.nupkg') ? overrides : {})) })
    assert.notEqual(run().status, 0, JSON.stringify(overrides))
  }
})

test('nothing to verify, or a missing kind of file, stops publication instead of passing', { skip }, t => {
  const without = name => Object.fromEntries(Object.entries(files).filter(([n]) => n !== name))
  for (const write of [{}, without('manifest.json'), without('consumer-proof.json'),
    { 'manifest.json': files['manifest.json'], 'consumer-proof.json': files['consumer-proof.json'] }]) {
    const result = setup(t, { attestations: all(), write })()
    assert.notEqual(result.status, 0, Object.keys(write).join(','))
    assert.equal(result.calls.length, 0, 'refused before verifying anything')
  }
})

const workflow = readFileSync(path.join(root, '.github/workflows/validate.yml'), 'utf8').replaceAll('\r\n', '\n')
// A job's text up to the next job, without comment lines (a comment above the next job belongs to that job).
const job = name => {
  const rest = workflow.split(`\n  ${name}:\n`)[1]
  assert.ok(rest, `validate.yml has a ${name} job`)
  return rest.split(/\n  [a-z][\w-]*:\n/)[0].split('\n').filter(line => !/^\s*#/.test(line)).join('\n')
}

test('only attest-libraries can sign, and it checks nothing out and runs no repository code', () => {
  const attest = job('attest-libraries')
  assert.match(attest, /^    needs: pack-libraries\n/)
  assert.match(attest, /\n    permissions:\n      contents: read\n      id-token: write\n      attestations: write\n    steps:\n/)
  assert.doesNotMatch(attest, /actions\/checkout|\n\s+run:|\n\s+shell:/)
  assert.deepEqual(attest.match(/uses: \S+/g), ['uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c',
    'uses: actions/attest@1e69f48acb82d1966a394da916b4c1698aa569d6'], 'both pinned by full commit id')
  assert.match(attest, /subject-path: \|\n\s+artifacts\/packages\/nuget\/\*\.nupkg\n\s+artifacts\/packages\/nuget\/manifest\.json\n\s+artifacts\/packages\/nuget\/consumer-proof\.json(\n|$)/)
  assert.doesNotMatch(job('publish-libraries'), /id-token|attestations: write|actions\/attest@/)
  assert.doesNotMatch(job('pack-libraries'), /id-token|attestations/)
})

test('attest-libraries runs exactly when publish-libraries does, on a push to main, and publish verifies before pushing', () => {
  const ifOf = text => text.match(/\n    if: (.*)\n/)[1]
  const publish = job('publish-libraries')
  // Owner ruling 2026-11-03 ("Keep push to main"): the T-579 trigger stays, with no ACTIONS_ENABLED switch.
  assert.equal(ifOf(job('attest-libraries')), "github.event_name == 'push' && github.ref == 'refs/heads/main'")
  assert.equal(ifOf(job('attest-libraries')), ifOf(publish))
  assert.match(publish, /^    needs: \[pack-libraries, attest-libraries\]\n/)
  assert.match(publish, /\n    permissions:\n      contents: read\n      packages: write\n      attestations: read\n/)
  const verify = publish.indexOf('run: bash tooling/verify-attestations.sh artifacts/packages/nuget')
  assert.ok(verify > 0, 'publish-libraries runs the verification')
  assert.ok(publish.indexOf('run: node tooling/library-promotion-proof.mjs') < verify, 'after the transferred-bytes proof')
  assert.ok(verify < publish.indexOf('dotnet nuget push'), 'before the push')
})
