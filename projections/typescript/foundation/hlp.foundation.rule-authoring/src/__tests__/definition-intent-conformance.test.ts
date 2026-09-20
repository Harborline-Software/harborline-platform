import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

import { serializeRuleDefinition, validateRuleDefinitionJson } from '../definition.js'

interface IntentCase {
  id: string
  sourceJson: string
  expected: { valid: boolean; code: string | null; location: string | null; canonicalJson?: string }
}

const corpus: { cases: IntentCase[] } = JSON.parse(readFileSync(new URL(
  '../../../../../../conformance/hlp.foundation.rule-authoring/definition-intent-cases.json', import.meta.url,
), 'utf8'))

describe.each(['Author', 'Publish', 'Persisted'] as const)('shared definition intent at %s', (phase) => {
  it.each(corpus.cases)('$id', ({ sourceJson, expected }) => {
    const result = validateRuleDefinitionJson(sourceJson, phase)
    expect(result.document !== null).toBe(expected.valid)
    if (expected.valid) {
      expect(result.diagnostics).toEqual([])
      const canonical = serializeRuleDefinition(result.document!)
      if (expected.canonicalJson !== undefined) expect(canonical).toBe(expected.canonicalJson)
      expect(JSON.parse(canonical)).toEqual(JSON.parse(sourceJson))
    } else {
      expect(result.diagnostics).toHaveLength(1)
      expect(result.diagnostics[0]).toMatchObject({ code: expected.code, location: expected.location, phase })
    }
  })
})
