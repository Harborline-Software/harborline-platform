import type { Json } from './model.js'

/** One child-table row. */
export interface RuleRow {
  id: string
  fields: Record<string, Json>
}

/**
 * A form instance the rule graph evaluates over (SPINE-1 §2.1) — TS mirror of the
 * .NET `RuleInstance`. A property whose value is a non-empty array of objects is a
 * child table (section id = property name; row id = each element's `_id`, else index).
 */
export class RuleInstance {
  fields: Record<string, Json> = {}
  tables: Record<string, RuleRow[]> = {}

  static fromJson(json: Record<string, Json>): RuleInstance {
    const instance = new RuleInstance()
    for (const [key, value] of Object.entries(json)) {
      if (Array.isArray(value) && value.length > 0 && value.every((e) => typeof e === 'object' && e !== null && !Array.isArray(e))) {
        const rows: RuleRow[] = value.map((el, i) => {
          const rowObj = el as Record<string, Json>
          const id = typeof rowObj['_id'] === 'string' ? rowObj['_id'] : String(i)
          const fields: Record<string, Json> = {}
          for (const [fk, fv] of Object.entries(rowObj)) {
            if (fk === '_id') continue
            fields[fk] = fv
          }
          return { id, fields }
        })
        instance.tables[key] = rows
      } else {
        instance.fields[key] = value
      }
    }
    return instance
  }
}
