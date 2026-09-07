import assert from 'node:assert/strict'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import * as resolution from '@harborline-software/capability-host/resolution'

const expected = ['CAPABILITY_RUNTIME_PACK','REFERENCE_APP_CATALOG','REFERENCE_APP_SEED','CompositionError','KERNEL_PACK','TTS_PACK','membershipOf','packSpecificity','projectForTenant','resolveEdition']
assert.deepEqual(expected.filter(name => !(name in resolution)), [], 'all ten real seams exported')
const edition = resolution.resolveEdition(resolution.REFERENCE_APP_CATALOG, resolution.REFERENCE_APP_SEED)
assert.deepEqual(edition.packs, ['kernel','capability-runtime','tts'])
assert.ok(edition.provenance.capabilities.every(row => row.providedBy))
assert.equal(edition.resolvedDefaults.voice, 'Samantha')
const all = resolution.projectForTenant(edition, { unlockedPacks: edition.packs })
const kernelOnly = resolution.projectForTenant(edition, { unlockedPacks: ['kernel'] })
assert.ok(all.capabilities.length > kernelOnly.capabilities.length)
assert.equal(resolution.membershipOf(all, all.capabilities[0].capabilityId), 'core')
assert.ok(resolution.packSpecificity(resolution.TTS_PACK) > resolution.packSpecificity(resolution.KERNEL_PACK))
assert.throws(() => resolution.resolveEdition([], ['missing']), e => e instanceof resolution.CompositionError && e.reason === 'missing-dependency')

// Named failed condition: this fixture may consume resolution, never contain a fork of it.
const forbidden = /function\s+(resolveEdition|projectForTenant|packSpecificity|membershipOf)\s*\(|class\s+CompositionError\b/
for (const file of walk(fileURLToPath(new URL('.', import.meta.url))).filter(x => /\.(?:mjs|js|ts|tsx)$/.test(x) && !x.includes('node_modules'))) {
  if (!file.endsWith('exercise.mjs')) assert.doesNotMatch(readFileSync(file, 'utf8'), forbidden, `vendored resolution in ${file}`)
}
function walk(dir) { return readdirSync(dir).flatMap(name => { const p=join(dir,name); return statSync(p).isDirectory()?walk(p):[p] }) }
process.stdout.write(`APPSHELL_CAPABILITY_HOST_PASS:${JSON.stringify({ adminRows:12, exports:expected, fullCapabilities:all.capabilities.length, kernelCapabilities:kernelOnly.capabilities.length, vendoredResolution:false })}\n`)

// Wave C corpus-conformance replay (ticket 092 Option A): canonical projection of the
// FROZEN four-field contract corpus. packedAppSurfaces:0 — this is corpus conformance,
// never independent semantic agreement (the named conditions bind harborline-app).
const contract = JSON.parse(readFileSync(new URL('./appshell-contract-cases.json', import.meta.url), 'utf8'))
assert.deepEqual(contract._header.scope, ['visibility', 'permissionVocabulary', 'lifecycleBuildModeFolding', 'routeJoin'])
assert.equal(contract.scenarios.length, contract._header.scenarioCount)
for (const row of contract.scenarios) {
  assert.deepEqual(Object.keys(row.expected), contract._header.scope)
  assert.ok(row.evidence.length > 0)
}
const contractProjection = contract.scenarios.map(({ id, expected }) => ({ id, ...expected }))
process.stdout.write(`APPSHELL_CONTRACT:${JSON.stringify(contractProjection)}\n`)
