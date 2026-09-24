/**
 * The generated function-and-field palette (DES-0018 rules-auth-20), TS lane. Mirrors the .NET
 * `RulesPaletteGenerator`: functions come from the R1 built-in register and references from the
 * Records fields, shaped by the rule's scope, so an author is offered only forms the compiler admits.
 */
import { builtInFunctions, type BuiltInFunctionDefinition, type RuleScope } from '@harborline-software/rule-engine'
import type { RuleDefinitionValueType as ColumnValueType } from './definition.js'

/** A field as Records owns it. The palette reads these facts and stores no copy of the schema. */
export interface RecordFieldFact { readonly key: string; readonly label: string; readonly valueType: ColumnValueType; readonly section?: string }
/** A child collection as Records owns it: the only thing an `agg` fold may range over. */
export interface RecordTableFact { readonly key: string; readonly columns: readonly RecordFieldFact[] }
export interface RecordFieldSet { readonly fields: readonly RecordFieldFact[]; readonly tables: readonly RecordTableFact[] }
export interface RulesPaletteReference { readonly id: string; readonly label: string; readonly valueType: ColumnValueType }
export interface RulesPalette { readonly functions: readonly BuiltInFunctionDefinition[]; readonly references: readonly RulesPaletteReference[] }

/** The folds `agg` accepts over a bounded child collection (rules-bound-4); mirrors the .NET register. */
export const aggregateFolds = ['sum', 'count', 'avg', 'min', 'max', 'any', 'all'] as const

const folds = (fold: string, type: ColumnValueType) =>
  fold === 'count' || ((fold === 'any' || fold === 'all') ? type === 'Boolean' : type === 'Number')

export function generatePalette(records: RecordFieldSet, scope: RuleScope, scopeTarget: string): RulesPalette {
  const references: RulesPaletteReference[] = []
  if (scope === 'Row') {
    const [tableKey, columnKey, extra] = scopeTarget.split('/')
    const table = extra === undefined ? records.tables.find((t) => t.key === tableKey) : undefined
    const self = table?.columns.find((c) => c.key === columnKey)
    if (self) references.push({ id: 'self', label: 'This field', valueType: self.valueType })
    for (const column of table?.columns ?? []) references.push({ id: `row.${column.key}`, label: `Row field ${column.label}`, valueType: column.valueType })
    for (const field of records.fields) references.push({ id: `parent.${field.key}`, label: `Parent field ${field.label}`, valueType: field.valueType })
  } else {
    const self = scope === 'Field' ? records.fields.find((f) => f.key === scopeTarget) : undefined
    if (self) references.push({ id: 'self', label: 'This field', valueType: self.valueType })
    for (const field of records.fields) {
      references.push(field.section === undefined
        ? { id: `field.${field.key}`, label: `Field ${field.label}`, valueType: field.valueType }
        : { id: `section.${field.section}.${field.key}`, label: `Section ${field.section} field ${field.label}`, valueType: field.valueType })
    }
  }
  for (const table of records.tables) {
    for (const column of table.columns) {
      for (const fold of aggregateFolds.filter((f) => folds(f, column.valueType))) {
        references.push({
          id: `table.${fold}(${table.key}.${column.key})`,
          label: `${fold[0].toUpperCase()}${fold.slice(1)} of ${table.key} ${column.label}`,
          valueType: fold === 'any' || fold === 'all' ? 'Boolean' : 'Number',
        })
      }
    }
  }
  return { functions: builtInFunctions.filter((f) => f.authorable), references }
}
