import { readFileSync } from 'node:fs'
import { canonicalJson, type Json } from '@harborline-software/rule-engine'
import { describe, expect, it } from 'vitest'

import { createFieldPreset, fieldPresetsFor, serializeRuleDefinition, validateRuleDefinitionJson,
  type FieldPreset, type RuleDefinitionDocument, type RuleDefinitionValueType } from '../index.js'

interface PresetCase {
  kind: RuleDefinitionValueType
  preset: FieldPreset
  source: RuleDefinitionDocument
  lowered: Json
}

const fixture: { cases: PresetCase[]; numberEdit: { threshold: string; lowered: Json } } = JSON.parse(readFileSync(new URL(
  '../../../../../../conformance/hlp.foundation.rule-authoring/field-presets.json', import.meta.url,
), 'utf8'))

describe('rules-auth-17 producer parity', () => {
  it.each(fixture.cases)('$kind source matches the literal authored fixture', ({ kind, preset, source }) => {
    expect(fieldPresetsFor(kind)).toEqual([preset])
    const document = createFieldPreset(structuredClone(source.envelope), 'amount', kind, preset)
    expect(document).toEqual(source)
    expect(serializeRuleDefinition(document)).toBe(canonicalJson(source as unknown as Json))
  })

  it.each([
    ['Number', 'NotBlank'], ['Number', 'MustBeTrue'],
    ['Text', 'NonNegative'], ['Text', 'MustBeTrue'],
    ['Boolean', 'NonNegative'], ['Boolean', 'NotBlank'],
  ] as const)('%s refuses incompatible %s', (kind, preset) => {
    expect(() => createFieldPreset(structuredClone(fixture.cases[0].source.envelope), 'amount', kind, preset)).toThrow()
  })

  it.each(fixture.cases)('$kind publishes ordinary source with literal lowered expression', ({ kind, preset, source, lowered }) => {
    const document = createFieldPreset(structuredClone(source.envelope), 'amount', kind, preset)
    const result = validateRuleDefinitionJson(serializeRuleDefinition(document), 'Publish')
    expect(result.document).not.toBeNull()
    expect(result.diagnostics).toEqual([])
    expect(result.lowered).toEqual(lowered)
    expect(document.envelope).toEqual(source.envelope)
    expect(result.document!.envelope).toEqual(source.envelope)
  })

  it('Number remains an ordinary editable Formula after changing the threshold to 5', () => {
    const source = fixture.cases[0].source
    const document = createFieldPreset(structuredClone(source.envelope), 'amount', 'Number', 'NonNegative')
    if (document.draft.kind !== 'Formula' || document.draft.expression?.kind !== 'Call') throw new Error('Expected ordinary Formula call')
    const threshold = document.draft.expression.args[1]
    if (threshold.kind !== 'Literal') throw new Error('Expected editable literal')
    threshold.value = fixture.numberEdit.threshold
    const result = validateRuleDefinitionJson(serializeRuleDefinition(document), 'Publish')
    expect(result.document).not.toBeNull()
    expect(result.diagnostics).toEqual([])
    expect(result.lowered).toEqual(fixture.numberEdit.lowered)
    expect(document.envelope).toEqual(source.envelope)
    expect(result.document!.envelope).toEqual(source.envelope)
  })
})
