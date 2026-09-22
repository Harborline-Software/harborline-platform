import { canonicalJson, compile, CompileError, DEFAULT_LIMITS, type Json, type RuleActionKind, type RuleScope, type RuleTier } from '@harborline-software/rule-engine'
import type { ArithOp, CompareOp, ColumnValueType, FormulaExpr, RuleDraft, TableCell } from './model.js'
import { compileDraft } from './compile.js'
import { DefinitionReadError, pointer, readDefinitionJson } from './definition-json.js'

export interface RuleDefinitionEnvelope {
  id: string
  version: string
  tenant: string
  cascadeLayer: string
  provenance: { [key: string]: Json }
  requires: string[]
}

export type RuleDefinitionValueType = 'Number' | 'Text' | 'Boolean'
export type RuleDefinitionExpression =
  | { kind: 'Ref'; name: string }
  | { kind: 'Literal'; value: string; valueType: RuleDefinitionValueType }
  | { kind: 'Binary'; op: ArithOp; left: RuleDefinitionExpression; right: RuleDefinitionExpression }
  | { kind: 'If'; when: { op: CompareOp; left: RuleDefinitionExpression; right: RuleDefinitionExpression }; then: RuleDefinitionExpression; else: RuleDefinitionExpression }

export type RuleDefinitionCell =
  | { kind: 'Any' }
  | { kind: 'Range'; lo: string; hi: string }
  | { kind: 'Compare'; op: CompareOp; value: string }

interface RuleDefinitionDraftBase {
  scope: RuleScope
  scopeTarget: string
  outputType: RuleActionKind
}

export interface RuleDefinitionFormula extends RuleDefinitionDraftBase {
  kind: 'Formula'
  inputs: { id: string; ref: string; type: RuleDefinitionValueType }[]
  expression: RuleDefinitionExpression | null
}

export interface RuleDefinitionTable extends RuleDefinitionDraftBase {
  kind: 'Table'
  hitPolicy: 'FirstMatch' | 'Priority'
  columns: { id: string; input: string; valueType: RuleDefinitionValueType }[]
  rows: { id: string; cells: Record<string, RuleDefinitionCell>; output: string; priority: number }[]
  noMatch: { kind: 'Default'; value: string } | { kind: 'CatchAll' }
}

/** Native wire source. Version admission belongs to the shared definition store. */
export interface RuleDefinitionDocument {
  envelope: RuleDefinitionEnvelope
  name: string
  tier: RuleTier
  draft: RuleDefinitionFormula | RuleDefinitionTable
}

export type RuleIntentPhase = 'Author' | 'Publish' | 'Persisted'
export interface RuleIntentDiagnostic {
  code: string
  location: string
  phase: RuleIntentPhase
  ruleId?: string
  cyclePath?: string[]
}
export interface RuleIntentResult {
  document: RuleDefinitionDocument | null
  diagnostics: RuleIntentDiagnostic[]
}

export const ruleIntentSchema = { limits: DEFAULT_LIMITS }

const invalid = 'rules.definition.invalid_document'
const badCell = 'rule.skin.decision_table_bad_cell'
const badExpression = 'rule.compile.invalid_expression'
const noMatch = 'rule.skin.no_match_unresolved'
const valueTypes = ['Number', 'Text', 'Boolean'] as const
const compareOps = ['==', '!=', '<', '<=', '>', '>='] as const

function refuse(code: string, location: string): never { throw new DefinitionReadError(code, location) }

function object(value: Json, location: string, allowed?: readonly string[]): Record<string, Json> {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) refuse(invalid, location)
  if (allowed) for (const key of Object.keys(value)) {
    if (!allowed.includes(key)) refuse('rules.definition.unknown_member', pointer(location, key))
  }
  return value
}

function member(value: Json, key: string, location: string): Json {
  const obj = object(value, location)
  if (!Object.hasOwn(obj, key)) refuse(invalid, pointer(location, key))
  return obj[key]
}

function text(value: Json, location: string): string {
  if (typeof value !== 'string') refuse(invalid, location)
  return value
}

function string(value: Json, key: string, location: string, nonblank = false): string {
  const result = text(member(value, key, location), pointer(location, key))
  if (nonblank && !result.trim()) refuse(invalid, pointer(location, key))
  return result
}

function array(value: Json, key: string, location: string): Json[] {
  const result = member(value, key, location)
  if (!Array.isArray(result)) refuse(invalid, pointer(location, key))
  return result
}

function choice<T extends string>(value: Json, key: string, location: string, choices: readonly T[], code = invalid): T {
  const result = member(value, key, location)
  if (typeof result !== 'string' || !choices.includes(result as T)) refuse(code, pointer(location, key))
  return result as T
}

function typedValue(value: string, type: RuleDefinitionValueType, location: string, code: string): void {
  if ((type === 'Number' && !Number.isFinite(Number(value))) ||
      (type === 'Boolean' && value !== 'true' && value !== 'false')) refuse(code, location)
}

function operator<T extends string>(value: Json, location: string, choices: readonly T[], code: string): T {
  const op = string(value, 'op', location)
  if (!choices.includes(op as T)) refuse(code, `${location}/op`)
  return op as T
}

function dictionary<T>(entries: [string, T][]): Record<string, T> {
  const result = Object.create(null) as Record<string, T>
  for (const [key, value] of entries) result[key] = value
  return result
}

function readExpression(value: Json, location: string, depth: number): RuleDefinitionExpression {
  if (depth > DEFAULT_LIMITS.maxAstNodes) refuse('rule.compile.ast_too_large', '/draft/expression')
  switch (string(value, 'kind', location)) {
    case 'Ref':
      object(value, location, ['kind', 'name'])
      return { kind: 'Ref', name: string(value, 'name', location, true) }
    case 'Literal': {
      object(value, location, ['kind', 'value', 'valueType'])
      const literal = string(value, 'value', location)
      const valueType = choice(value, 'valueType', location, valueTypes)
      typedValue(literal, valueType, `${location}/value`, badExpression)
      return { kind: 'Literal', value: literal, valueType }
    }
    case 'Binary': {
      object(value, location, ['kind', 'op', 'left', 'right'])
      const op = operator(value, location, ['+', '-', '*', '/'] as const, badExpression)
      return { kind: 'Binary', op,
        left: readExpression(member(value, 'left', location), `${location}/left`, depth + 1),
        right: readExpression(member(value, 'right', location), `${location}/right`, depth + 1) }
    }
    case 'If': {
      object(value, location, ['kind', 'when', 'then', 'else'])
      const path = `${location}/when`
      const condition = object(member(value, 'when', location), path, ['op', 'left', 'right'])
      const left = readExpression(member(condition, 'left', path), `${path}/left`, depth + 1)
      const op = operator(condition, path, compareOps, badExpression)
      const right = readExpression(member(condition, 'right', path), `${path}/right`, depth + 1)
      return { kind: 'If', when: { left, op, right },
        then: readExpression(member(value, 'then', location), `${location}/then`, depth + 1),
        else: readExpression(member(value, 'else', location), `${location}/else`, depth + 1) }
    }
    default: return refuse(invalid, `${location}/kind`)
  }
}

function readCell(value: Json, location: string): RuleDefinitionCell {
  switch (string(value, 'kind', location)) {
    case 'Any':
      object(value, location, ['kind'])
      return { kind: 'Any' }
    case 'Compare':
      object(value, location, ['kind', 'op', 'value'])
      return { kind: 'Compare', op: operator(value, location, compareOps, badCell), value: string(value, 'value', location) }
    case 'Range': {
      object(value, location, ['kind', 'lo', 'hi'])
      const endpoint = (key: string) => {
        const result = string(value, key, location)
        if (result.trim() && !Number.isFinite(Number(result))) refuse(badCell, `${location}/${key}`)
        return result
      }
      return { kind: 'Range', lo: endpoint('lo'), hi: endpoint('hi') }
    }
    default: return refuse(badCell, `${location}/kind`)
  }
}

function readDraft(value: Json): RuleDefinitionDocument['draft'] {
  const path = '/draft'
  const kind = choice(value, 'kind', path, ['Formula', 'Table'] as const)
  object(value, path, kind === 'Formula'
    ? ['kind', 'scope', 'scopeTarget', 'outputType', 'inputs', 'expression']
    : ['kind', 'scope', 'scopeTarget', 'outputType', 'hitPolicy', 'columns', 'rows', 'noMatch'])
  const scope = choice(value, 'scope', path, ['Field', 'Section', 'Schema', 'Row', 'Table'] as const, 'rule.compile.bad_grammar')
  const scopeTarget = string(value, 'scopeTarget', path)
  const outputType = choice(value, 'outputType', path, ['Visibility', 'Required', 'ReadOnly', 'Validate', 'Compute', 'Presentation', 'Options'] as const, 'rule.compile.unknown_action')
  if (kind === 'Formula') {
    const inputs = array(value, 'inputs', path).map((input, i) => {
      const p = `${path}/inputs/${i}`
      object(input, p, ['id', 'ref', 'type'])
      return { id: string(input, 'id', p, true), ref: string(input, 'ref', p, true), type: choice(input, 'type', p, valueTypes) }
    })
    const expression = member(value, 'expression', path)
    return { kind, scope, scopeTarget, outputType, inputs,
      expression: expression === null ? null : readExpression(expression, `${path}/expression`, 0) }
  }
  const columns = array(value, 'columns', path).map((column, i) => {
    const p = `${path}/columns/${i}`
    object(column, p, ['id', 'input', 'valueType'])
    return { id: string(column, 'id', p, true), input: string(column, 'input', p, true), valueType: choice(column, 'valueType', p, valueTypes) }
  })
  const rows = array(value, 'rows', path).map((row, i) => {
    const p = `${path}/rows/${i}`
    object(row, p, ['id', 'cells', 'output', 'priority'])
    const cells = dictionary(Object.entries(object(member(row, 'cells', p), `${p}/cells`)).map(([key, cell]): [string, RuleDefinitionCell] => {
      const cp = pointer(`${p}/cells`, key)
      const column = columns.find((c) => c.id === key)
      if (!column) refuse(badCell, cp)
      const decoded = readCell(cell, cp)
      if (decoded.kind === 'Compare') typedValue(decoded.value, column.valueType, `${cp}/value`, badCell)
      return [key, decoded]
    }))
    const priority = member(row, 'priority', p)
    if (typeof priority !== 'number' || !Number.isInteger(priority) || priority < -2147483648 || priority > 2147483647) refuse(invalid, `${p}/priority`)
    return { id: string(row, 'id', p, true), cells, output: string(row, 'output', p), priority }
  })
  const posture = member(value, 'noMatch', path)
  const pp = `${path}/noMatch`
  const postureKind = string(posture, 'kind', pp)
  let noMatchPosture: RuleDefinitionTable['noMatch']
  if (postureKind === 'Default') {
    object(posture, pp, ['kind', 'value'])
    noMatchPosture = { kind: 'Default', value: string(posture, 'value', pp) }
  } else if (postureKind === 'CatchAll') {
    object(posture, pp, ['kind'])
    noMatchPosture = { kind: 'CatchAll' }
  } else return refuse(noMatch, `${pp}/kind`)
  return { kind, scope, scopeTarget, outputType, columns, rows, noMatch: noMatchPosture,
    hitPolicy: choice(value, 'hitPolicy', path, ['Priority', 'FirstMatch'] as const, 'rule.skin.decision_table_invalid_hit_policy') }
}

function readDocument(value: Json): RuleDefinitionDocument {
  const root = object(value, '', ['envelope', 'name', 'tier', 'draft'])
  const p = '/envelope'
  const metadata = object(member(root, 'envelope', ''), p, ['id', 'version', 'tenant', 'cascadeLayer', 'provenance', 'requires'])
  const version = string(metadata, 'version', p)
  const provenance = object(member(metadata, 'provenance', p), `${p}/provenance`)
  const envelope = { id: string(metadata, 'id', p, true), version, tenant: string(metadata, 'tenant', p, true),
    cascadeLayer: string(metadata, 'cascadeLayer', p, true), provenance,
    requires: array(metadata, 'requires', p).map((item, i) => text(item, `${p}/requires/${i}`)) }
  return { envelope, name: string(root, 'name', '', true),
    tier: choice(root, 'tier', '', ['JsonSchema', 'JsonLogic', 'PowerFx'] as const, 'rule.compile.unsupported_tier'),
    draft: readDraft(member(root, 'draft', '')) }
}

const editorType: Record<RuleDefinitionValueType, ColumnValueType> = { Number: 'number', Text: 'text', Boolean: 'boolean' }

function editorExpression(value: RuleDefinitionExpression): FormulaExpr {
  switch (value.kind) {
    case 'Ref': return { kind: 'ref', ref: value.name }
    case 'Literal': return { kind: 'literal', value: value.value, valueType: editorType[value.valueType] }
    case 'Binary': return { kind: 'binary', op: value.op, left: editorExpression(value.left), right: editorExpression(value.right) }
    case 'If': return { kind: 'if', when: { op: value.when.op, left: editorExpression(value.when.left), right: editorExpression(value.when.right) }, then: editorExpression(value.then), else: editorExpression(value.else) }
  }
}

function editorDraft(draft: RuleDefinitionDocument['draft']): RuleDraft {
  const common = { scope: draft.scope, scopeTarget: draft.scopeTarget, outputType: draft.outputType }
  if (draft.kind === 'Formula') return { ...common, skin: 'formula',
    inputs: draft.inputs.map((input) => ({ ...input, type: editorType[input.type] })),
    expression: draft.expression === null ? null : editorExpression(draft.expression) }
  return { ...common, skin: 'table', hitPolicy: draft.hitPolicy === 'Priority' ? 'priority' : 'first-match',
    columns: draft.columns.map((column) => ({ ...column, valueType: editorType[column.valueType] })),
    rows: draft.rows.map((row) => ({ ...row, cells: dictionary(Object.entries(row.cells).map(([key, cell]): [string, TableCell] => {
      switch (cell.kind) {
        case 'Any': return [key, { kind: 'any' }]
        case 'Range': return [key, { kind: 'range', lo: cell.lo, hi: cell.hi }]
        case 'Compare': return [key, { kind: 'compare', op: cell.op, value: cell.value }]
      }
    })) })),
    noMatch: draft.noMatch.kind === 'Default' ? { kind: 'default', value: draft.noMatch.value } : { kind: 'catch-all' } }
}

function compileLocation(code: string, draft: RuleDefinitionDocument['draft']): string {
  switch (code) {
    case 'rule.skin.no_match_unresolved': return '/draft/noMatch'
    case 'rule.skin.decision_table_invalid_hit_policy': return '/draft/hitPolicy'
    case 'rule.skin.decision_table_no_inputs': return '/draft/columns'
    case 'rule.skin.decision_table_empty':
    case 'rule.skin.decision_table_bad_cell':
    case 'rule.skin.decision_table_bad_row': return '/draft/rows'
    default: return draft.kind === 'Formula' ? '/draft/expression' : '/draft'
  }
}

export function validateRuleDefinitionJson(json: string, phase: RuleIntentPhase): RuleIntentResult {
  let document: RuleDefinitionDocument
  try {
    document = readDocument(readDefinitionJson(json))
  } catch (error) {
    if (error instanceof DefinitionReadError) return { document: null, diagnostics: [{ code: error.code, location: error.location, phase }] }
    if (error instanceof SyntaxError) return { document: null, diagnostics: [{ code: invalid, location: '', phase }] }
    throw error
  }
  const ruleId = document.envelope.id
  if (document.tier !== 'JsonLogic') return { document: null, diagnostics: [{ code: 'rule.compile.unsupported_tier', location: '/tier', phase, ruleId }] }
  const draft = document.draft
  if (draft.kind === 'Table') {
    const resolved = draft.noMatch.kind === 'Default' ? draft.noMatch.value.trim().length > 0
      : draft.rows.some((row) => draft.columns.every((column) => !Object.hasOwn(row.cells, column.id) || row.cells[column.id].kind === 'Any'))
    if (!resolved) return { document: null, diagnostics: [{ code: noMatch, location: '/draft/noMatch', phase, ruleId }] }
  }
  try {
    const lowered = compileDraft(editorDraft(draft), ruleId)
    // compileDraft already emits the canonical encoded scalar JSON contract used by the
    // compiler. Re-encoding here adds two quotes and changes the literal admission bound.
    compile([lowered])
    return { document, diagnostics: [] }
  } catch (error) {
    if (!(error instanceof CompileError)) throw error
    return { document: null, diagnostics: [{ code: error.code, location: compileLocation(error.code, draft), phase,
      ruleId: error.ruleId ?? ruleId, ...(error.cyclePath ? { cyclePath: [...error.cyclePath] } : {}) }] }
  }
}

export function serializeRuleDefinition(document: RuleDefinitionDocument): string {
  return canonicalJson(document as unknown as Json)
}
