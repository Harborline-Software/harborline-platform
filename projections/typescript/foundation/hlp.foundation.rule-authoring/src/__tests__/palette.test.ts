import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import { builtInFunctions, resolveBuiltIn, type RuleScope } from '@harborline-software/rule-engine'
import { formulaCallOps, generatePalette, type RecordFieldSet } from '../index.js'

const here = dirname(fileURLToPath(import.meta.url))
const fixture = JSON.parse(readFileSync(join(here, '..', '..', '..', '..', '..', '..', 'conformance', 'hlp.blocks.builder-definitions', 'rules-editor-contract-fixtures.json'), 'utf8')) as {
  referenceForms: { palette: { id: string; label: string; valueType: string }[]; cases: { id: string; ref: string; scope: RuleScope; scopeTarget: string }[] }
}

export const fixtureRecords: RecordFieldSet = {
  fields: [{ key: 'total', label: 'total', valueType: 'Number' }, { key: 'x', label: 'x', valueType: 'Number' }, { key: 'z', label: 'z', valueType: 'Number' }, { key: 'f', label: 'f', valueType: 'Number', section: 's' }],
  tables: [{ key: 'lines', columns: [{ key: 'y', label: 'y', valueType: 'Number' }, { key: 'amount', label: 'amount', valueType: 'Number' }] }],
}

describe('T-590 generated palette (TS lane)', () => {
  it('rules-auth-20, rules-auth-3: the generated palette produces every reference-form entry of the editor contract fixture', () => {
    const produced = new Set<string>()
    for (const item of fixture.referenceForms.cases) {
      const palette = generatePalette(fixtureRecords, item.scope, item.scopeTarget)
      expect(palette.references.map((r) => r.id), `${item.id}: cannot produce ${item.ref}`).toContain(item.ref)
      for (const r of palette.references) produced.add(JSON.stringify([r.id, r.label, r.valueType]))
    }
    for (const entry of fixture.referenceForms.palette) expect(produced).toContain(JSON.stringify([entry.id, entry.label, entry.valueType]))
  })

  it('rules-auth-20, rules-auth-13: references follow the rule scope and name only Records fields', () => {
    const field = generatePalette(fixtureRecords, 'Field', 'total').references.map((r) => r.id)
    expect(field.some((id) => id.startsWith('row.') || id.startsWith('parent.'))).toBe(false)
    expect(field).toContain('section.s.f')
    expect(field).not.toContain('field.f')
    const row = generatePalette(fixtureRecords, 'Row', 'lines/amount').references.map((r) => r.id)
    expect(row).toContain('row.y')
    expect(row).toContain('parent.z')
    expect(row.some((id) => id.startsWith('field.'))).toBe(false)
    expect(generatePalette({ fields: [], tables: [] }, 'Field', 'total').references).toEqual([])
  })

  it('rules-auth-20, rules-eng-27: the function half of the palette is the register’s authorable built-ins', () => {
    const palette = generatePalette(fixtureRecords, 'Field', 'total')
    expect(palette.functions.map((f) => f.key)).toEqual(builtInFunctions.filter((f) => f.key !== 'var').map((f) => f.key))
    expect([...formulaCallOps]).toEqual(palette.functions.map((f) => f.key))
    for (const f of palette.functions) expect(resolveBuiltIn(f.key)).toBe(f.reference)
  })
})
