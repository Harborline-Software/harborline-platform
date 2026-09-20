import { describe, expect, it } from 'vitest'
import { DEFAULT_LIMITS } from '@harborline-software/rule-engine'

import { validateRuleDefinitionJson, serializeRuleDefinition, ruleIntentSchema } from '../definition.js'

function formula() {
  return {
    envelope: {
      id: 'invoice-total', version: '1.2.3', tenant: 'tenant-a', cascadeLayer: 'domain-package',
      provenance: { kind: 'package', id: 'finance' }, requires: ['records.invoice@2.0.0'],
    },
    name: 'Invoice total', tier: 'JsonLogic',
    draft: {
      kind: 'Formula', scope: 'Field', scopeTarget: 'total', outputType: 'Compute',
      inputs: [{ id: 'amount', ref: 'field.amount', type: 'Number' }],
      expression: { kind: 'Ref', name: 'field.amount' },
    },
  }
}

function table() {
  return {
    ...formula(),
    draft: {
      kind: 'Table', scope: 'Field', scopeTarget: 'total', outputType: 'Compute', hitPolicy: 'Priority',
      columns: [{ id: 'amount', input: 'field.amount', valueType: 'Number' }],
      rows: [{ id: 'low', cells: { amount: { kind: 'Range', lo: '0', hi: '100' } }, output: 'low', priority: 1 }],
      noMatch: { kind: 'Default', value: 'high' },
    },
  }
}

function set(source: object, path: string[], value: unknown): void {
  let parent = source as Record<string, unknown>
  for (const member of path.slice(0, -1)) parent = parent[member] as Record<string, unknown>
  parent[path[path.length - 1]] = value
}

describe('provider-neutral Rules definition intent', () => {
  it('admits authored source without a consumer resolution policy', () => {
    const source: Record<string, unknown> = formula()
    delete source.versionPolicy

    const result = validateRuleDefinitionJson(JSON.stringify(source), 'Author')

    expect(result.diagnostics).toEqual([])
    expect(result.document).not.toBeNull()
    expect(JSON.parse(serializeRuleDefinition(result.document!))).toEqual(source)
  })

  it.each([['formula', formula], ['table', table]] as const)('round-trips the native %s source without losing envelope or skin', (_name, create) => {
    const source = create()
    const result = validateRuleDefinitionJson(JSON.stringify(source), 'Author')
    expect(result.diagnostics).toEqual([])
    expect(result.document).toEqual(source)
    const canonical = serializeRuleDefinition(result.document!)
    expect(JSON.parse(canonical)).toEqual(source)
    expect(serializeRuleDefinition(validateRuleDefinitionJson(canonical, 'Persisted').document!)).toBe(canonical)
  })

  it.each(['1.0.0-alpha.10', '1.0.0+build.01', '2147483648.0.0'])('preserves the generic-store version label %s', (version) => {
    const source = formula()
    source.envelope.version = version
    const result = validateRuleDefinitionJson(JSON.stringify(source), 'Author')
    expect(result.diagnostics).toEqual([])
    expect(result.document?.envelope.version).toBe(version)
  })

  it.each([
    [['tier'], 'mystery', 'rule.compile.unsupported_tier', '/tier'],
    [['tier'], 'PowerFx', 'rule.compile.unsupported_tier', '/tier'],
    [['tier'], 'JsonSchema', 'rule.compile.unsupported_tier', '/tier'],
    [['draft', 'scope'], 'mystery', 'rule.compile.bad_grammar', '/draft/scope'],
    [['draft', 'outputType'], 'mystery', 'rule.compile.unknown_action', '/draft/outputType'],
    [['envelope', 'retentionClass'], 'tenant-choice', 'rules.definition.unknown_member', '/envelope/retentionClass'],
    [['envelope', 'legalHold'], true, 'rules.definition.unknown_member', '/envelope/legalHold'],
    [['draft', 'expression'], { kind: 'Literal', value: 'tru', valueType: 'Boolean' }, 'rule.compile.invalid_expression', '/draft/expression/value'],
    [['draft', 'expression'], { kind: 'Literal', value: 'NaN', valueType: 'Number' }, 'rule.compile.invalid_expression', '/draft/expression/value'],
    [['draft', 'expression'], { kind: 'Binary', op: 'regex', left: { kind: 'Literal', value: '1', valueType: 'Number' }, right: { kind: 'Literal', value: '2', valueType: 'Number' } }, 'rule.compile.invalid_expression', '/draft/expression/op'],
  ] as const)('refuses malformed formula source at %s', (path, value, code, location) => {
    const source = formula()
    set(source, [...path], value)
    for (const phase of ['Author', 'Publish', 'Persisted'] as const) {
      const result = validateRuleDefinitionJson(JSON.stringify(source), phase)
      expect(result.document).toBeNull()
      expect(result.diagnostics).toHaveLength(1)
      expect(result.diagnostics[0]).toMatchObject({ code, location, phase })
    }
  })

  it.each([
    [['draft', 'rows', '0', 'cells', 'amount', 'kind'], 'mystery', 'rule.skin.decision_table_bad_cell', '/draft/rows/0/cells/amount/kind'],
    [['draft', 'rows', '0', 'cells', 'amount', 'hi'], 'not-a-number', 'rule.skin.decision_table_bad_cell', '/draft/rows/0/cells/amount/hi'],
    [['draft', 'rows', '0', 'cells', 'missing/column'], { kind: 'Any' }, 'rule.skin.decision_table_bad_cell', '/draft/rows/0/cells/missing~1column'],
    [['draft', 'hitPolicy'], 'Collect', 'rule.skin.decision_table_invalid_hit_policy', '/draft/hitPolicy'],
    [['draft', 'noMatch'], { kind: 'Default', value: '' }, 'rule.skin.no_match_unresolved', '/draft/noMatch'],
  ] as const)('refuses malformed table source at %s', (path, value, code, location) => {
    const source = table()
    set(source, [...path], value)
    const result = validateRuleDefinitionJson(JSON.stringify(source), 'Publish')
    expect(result.document).toBeNull()
    expect(result.diagnostics).toHaveLength(1)
    expect(result.diagnostics[0]).toMatchObject({ code, location, phase: 'Publish' })
  })

  it('rejects duplicate members instead of selecting the last declaration', () => {
    const json = JSON.stringify(formula()).replace('"tier":"JsonLogic"', '"tier":"PowerFx","tier":"JsonLogic"')
    const result = validateRuleDefinitionJson(json, 'Publish')
    expect(result.document).toBeNull()
    expect(result.diagnostics).toEqual([{ code: 'rules.definition.duplicate_member', location: '/tier', phase: 'Publish' }])
  })

  it('uses compiler-owned literal bounds and does not clamp persisted source', () => {
    expect(ruleIntentSchema.limits.maxLiteralLength).toBe(4096)
    const source = formula()
    set(source, ['draft', 'expression'], { kind: 'Literal', value: 'x'.repeat(4096), valueType: 'Text' })
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Publish').diagnostics).toEqual([])
    set(source, ['draft', 'expression'], { kind: 'Literal', value: 'x'.repeat(4097), valueType: 'Text' })
    const json = JSON.stringify(source)
    const result = validateRuleDefinitionJson(json, 'Persisted')
    expect(result.document).toBeNull()
    expect(result.diagnostics[0]).toMatchObject({ code: 'rule.compile.literal_too_long', location: '/draft/expression', phase: 'Persisted', ruleId: 'invoice-total' })
    expect(JSON.stringify(source)).toBe(json)
  })

  it('runs the full dependency compiler and reports a self-cycle', () => {
    const source = formula()
    source.draft.inputs[0].ref = 'field.total'
    source.draft.expression.name = 'field.total'
    const result = validateRuleDefinitionJson(JSON.stringify(source), 'Publish')
    expect(result.document).toBeNull()
    expect(result.diagnostics[0]).toMatchObject({ code: 'rule.compile.cycle', location: '/draft/expression', ruleId: 'invoice-total' })
    expect(result.diagnostics[0].cyclePath?.length).toBeGreaterThan(0)
  })

  it('does not turn an unreachable-row warning into an admission refusal', () => {
    const source = table()
    set(source, ['draft', 'noMatch'], { kind: 'CatchAll' })
    set(source, ['draft', 'rows'], [
      { id: 'wildcard', cells: {}, output: 'first', priority: 10 },
      { id: 'conditional', cells: { amount: { kind: 'Range', lo: '0', hi: '100' } }, output: 'second', priority: 1 },
    ])
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Publish').diagnostics).toEqual([])
  })

  it.each([
    ['"kind":"Ref"', '"kind":"Literal","k\\u0069nd":"Ref"', '/draft/expression/kind'],
    ['"amount":{"kind":"Range"', '"amount":{},"\\u0061mount":{"kind":"Range"', '/draft/rows/0/cells/amount'],
    ['"id":"finance"', '"a~/":1,"a\\u007e/":2,"id":"finance"', '/envelope/provenance/a~0~1'],
  ])('rejects nested escaped-equivalent members at %s', (before, after, location) => {
    const source = location.includes('/rows/') ? table() : formula()
    const result = validateRuleDefinitionJson(JSON.stringify(source).replace(before, after), 'Author')
    expect(result).toEqual({ document: null, diagnostics: [{ code: 'rules.definition.duplicate_member', location, phase: 'Author' }] })
  })

  it.each(['', '{', '{} trailing', '{"a":1,}', '[1,]', '{"a":01}', '{"a":+1}', '{"a":.1}', '{"a":NaN}', '{"a":undefined}', '{"a":"\\x20"}', '{"a":"\n"}', '{/*comment*/}', '\u00a0{}', 'null', '[]', 'true'])('refuses invalid document %j', (json) => {
    expect(validateRuleDefinitionJson(json, 'Persisted')).toEqual({ document: null, diagnostics: [{ code: 'rules.definition.invalid_document', location: '', phase: 'Persisted' }] })
  })

  it.each(['Compute', 'Validate', 'Presentation', 'Options', 'Visibility', 'Required', 'ReadOnly'])('admits existing engine action %s', (action) => {
    const source = formula()
    source.draft.outputType = action
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Publish').document).toEqual(source)
  })

  it.each([['Field', 'total'], ['Section', 'invoice'], ['Schema', ''], ['Row', 'lines/total'], ['Table', 'lines/sum/total']])('admits native scope %s', (scope, target) => {
    const source = formula()
    source.draft.scope = scope
    source.draft.scopeTarget = target
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Publish').document).toEqual(source)
  })

  it.each([['scope', 'field', 'rule.compile.bad_grammar'], ['scope', '0', 'rule.compile.bad_grammar'], ['outputType', 'compute', 'rule.compile.unknown_action'], ['outputType', '0', 'rule.compile.unknown_action'], ['scope', 0, 'rule.compile.bad_grammar'], ['outputType', 0, 'rule.compile.unknown_action']])('refuses non-exact %s %s', (member, value, code) => {
    const source = formula()
    set(source, ['draft', member as string], value)
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Author').diagnostics).toEqual([{ code, location: `/draft/${member}`, phase: 'Author' }])
  })

  it.each([['Number', 'not-a-number'], ['Number', 'Infinity'], ['Boolean', 'TRUE'], ['Boolean', 'tru']])('refuses malformed %s comparison %s', (valueType, value) => {
    const source = table()
    source.draft.columns[0].valueType = valueType
    set(source, ['draft', 'rows', '0', 'cells', 'amount'], { kind: 'Compare', op: '==', value })
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Author').diagnostics).toEqual([{ code: 'rule.skin.decision_table_bad_cell', location: '/draft/rows/0/cells/amount/value', phase: 'Author' }])
  })

  it('refuses unsupported If comparison before lowering', () => {
    const source = formula()
    const literal = { kind: 'Literal', value: '1', valueType: 'Number' }
    set(source, ['draft', 'expression'], { kind: 'If', when: { op: 'regex', left: literal, right: literal }, then: literal, else: literal })
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Author').diagnostics).toEqual([{ code: 'rule.compile.invalid_expression', location: '/draft/expression/when/op', phase: 'Author' }])
  })

  it('enforces the full compiler AST boundary at 256/257 nodes', () => {
    const source = formula()
    // Each binary contributes its object, argument array and one literal: 3 nodes.
    // 85 binaries + one literal = 256; replacing the leaf with a var adds one.
    const chain = (leaf: object): object => {
      let expression = leaf
      for (let i = 0; i < 85; i++) expression = { kind: 'Binary', op: '+', left: expression, right: { kind: 'Literal', value: '1', valueType: 'Number' } }
      return expression
    }
    set(source, ['draft', 'expression'], chain({ kind: 'Literal', value: '1', valueType: 'Number' }))
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Publish').document).toEqual(source)
    set(source, ['draft', 'expression'], chain({ kind: 'Ref', name: 'field.amount' }))
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Publish').diagnostics).toEqual([{ code: 'rule.compile.ast_too_large', location: '/draft/expression', phase: 'Publish', ruleId: 'invoice-total' }])
  })

  it('keeps arbitrary provenance, special map names, array order and detached source', () => {
    const source = table()
    set(source, ['envelope', 'provenance'], JSON.parse('{"__proto__":{"safe":true},"constructor":[3,1,2],"nested":{"extra":null}}'))
    source.draft.columns[0].id = '__proto__'
    set(source, ['draft', 'rows', '0', 'cells'], JSON.parse('{"__proto__":{"kind":"Any"}}'))
    source.envelope.requires = ['z@1', 'a@2']
    const json = JSON.stringify(source)
    const first = validateRuleDefinitionJson(json, 'Author').document!
    expect(first).toEqual(source)
    expect(JSON.parse(serializeRuleDefinition(first))).toEqual(source)
    first.envelope.requires.push('changed')
    first.envelope.provenance.extra = 'changed'
    expect(JSON.stringify(source)).toBe(json)
    expect(validateRuleDefinitionJson(json, 'Author').document).toEqual(source)
    expect(Object.prototype).not.toHaveProperty('safe')
  })

  it.each(['Author', 'Publish', 'Persisted'] as const)('refuses a consumer selector in authored source at %s', phase => {
    const source = { ...formula(), versionPolicy: { kind: 'Latest', version: null } }
    expect(validateRuleDefinitionJson(JSON.stringify(source), phase).diagnostics).toEqual([
      { code: 'rules.definition.unknown_member', location: '/versionPolicy', phase },
    ])
  })

  it('exposes the engine limit object and preserves authored version labels', () => {
    expect(ruleIntentSchema.limits).toBe(DEFAULT_LIMITS)
    const source = formula()
    set(source, ['envelope', 'version'], '2147483648.0.0-alpha.10+build.01')
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Publish').document).toEqual(source)
  })

  it.each(['__proto__', 'constructor', 'toString'])('treats an absent special-name cell %s as a wildcard', (id) => {
    const source = table()
    source.draft.columns[0].id = id
    set(source, ['draft', 'rows', '0', 'cells'], {})
    set(source, ['draft', 'noMatch'], { kind: 'CatchAll' })
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Publish').document).toEqual(source)
  })

  it.each(['table', 'formula', 'if'])('retains native invalid-document pointers for a non-string %s operator', (kind) => {
    const source = kind === 'table' ? table() : formula()
    const literal = { kind: 'Literal', value: '1', valueType: 'Number' }
    const location = kind === 'table' ? '/draft/rows/0/cells/amount/op' : kind === 'if' ? '/draft/expression/when/op' : '/draft/expression/op'
    if (kind === 'table') set(source, ['draft', 'rows', '0', 'cells', 'amount'], { kind: 'Compare', op: 0, value: '1' })
    else set(source, ['draft', 'expression'], kind === 'if'
      ? { kind: 'If', when: { op: 0, left: literal, right: literal }, then: literal, else: literal }
      : { kind: 'Binary', op: 0, left: literal, right: literal })
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Persisted').diagnostics).toEqual([{ code: 'rules.definition.invalid_document', location, phase: 'Persisted' }])
  })

  it('refuses unknown nested members with escaped pointers', () => {
    const source = formula()
    set(source, ['draft', 'expression', 'extra~/'], true)
    expect(validateRuleDefinitionJson(JSON.stringify(source), 'Author').diagnostics).toEqual([{ code: 'rules.definition.unknown_member', location: '/draft/expression/extra~0~1', phase: 'Author' }])
  })

  it('bounds nested JSON and gives malformed syntax precedence over duplicates', () => {
    expect(validateRuleDefinitionJson('['.repeat(1025) + '0' + ']'.repeat(1025), 'Author').diagnostics).toEqual([{ code: 'rules.definition.invalid_document', location: '', phase: 'Author' }])
    expect(validateRuleDefinitionJson('{"a":1,"a":2,}', 'Author').diagnostics).toEqual([{ code: 'rules.definition.invalid_document', location: '', phase: 'Author' }])
  })
})
