import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import * as contracts from '../dist/index.js'

const cases = JSON.parse(readFileSync(new URL('../../../../../conformance/hlp.copilot/delegation-cases.json', import.meta.url), 'utf8'))

for (const envelope of cases.envelopes) {
  test(`pilot-ck-15 pilot-ck-18: ${envelope.name}; oracle: RFC 8785 fixture bytes and independent shasum literal`, async () => {
    assert.equal(contracts.serializeProposalCanonical(envelope.value), envelope.canonical)
    assert.equal(await contracts.proposalDigest(envelope.value), envelope.digest)
  })
}
