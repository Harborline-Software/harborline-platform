// Oracles: T-1138 release.shared-signer-policy; ADR-0198 decision 5.
import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import test from 'node:test'

const workflow = name => readFileSync(new URL(`../../.github/workflows/${name}.yml`, import.meta.url), 'utf8')
const job = (text, name) => text.split(`\n  ${name}:\n`)[1]?.split(/\n  [a-z][a-z0-9-]*:\n/)[0]

test('release.shared-signer-policy: signing has no App credential or publishing environment', () => {
  const signer = workflow('sign-gallery-receipt')
  assert.doesNotMatch(signer, /secrets:|secrets\.|environment:|create-github-app-token|--receipt post/)
  assert.equal((signer.match(/id-token: write/g) ?? []).length, 1)
  assert.equal((signer.match(/attestations: write/g) ?? []).length, 1)
  assert.doesNotMatch(signer, /contents: write|checks: write|statuses: write|pnpm install|run-phase-4-gate/)
})

test('release.shared-signer-policy: the calling job is step-free and passes no secrets', () => {
  const caller = job(workflow('verify-gallery-receipt'), 'sign')
  assert.ok(caller)
  assert.match(caller, /uses: \.\/\.github\/workflows\/sign-gallery-receipt.yml/)
  assert.match(caller, /id-token: write/)
  assert.match(caller, /attestations: write/)
  assert.doesNotMatch(caller, /steps:|secrets:|environment:/)
})

test('release.shared-signer-policy: a separate main poster checks evidence with no signing rights', () => {
  const poster = job(workflow('verify-gallery-receipt'), 'post')
  assert.ok(poster, 'dedicated App poster must be a separate job')
  assert.match(poster, /environment: gallery-receipt/)
  assert.match(poster, /ref: \$\{\{ github.sha \}\}/)
  assert.match(poster, /GALLERY_RECEIPT_APP_KEY/)
  assert.match(poster, /--receipt post/)
  assert.doesNotMatch(poster, /id-token:|attestations:|contents: write|run-phase-4-gate|pnpm install/)
  assert.match(poster, /needs\.sign\.outputs\.artifact-id/)
})
