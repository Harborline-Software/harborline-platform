import type { Json } from './model.js'
import { Codes } from './codes.js'
import { parseBoundedJsonText } from './input-envelope.js'

const instanceBrand = new WeakSet<object>()
const valueBrand = new WeakSet<object>()
const rowBrand = new WeakSet<object>()
const instanceData = new WeakMap<object, { fields: Record<string, Json>, tables: Record<string, RuleRow[]> }>()
const valueData = new WeakMap<object, Json>()
const rowData = new WeakMap<object, RuleRow>()
/** One child-table row. */
export interface RuleRow {
  id: string
  fields: Record<string, Json>
  hasExplicitId: boolean
}

/** One JSON-text-captured value accepted by reactive graph mutation. */
export class RuleValueSnapshot {
  private constructor() {}

  static fromJsonText(jsonText: string): RuleValueSnapshot {
    const parsed = parseBoundedJsonText(jsonText, 'rule value')
    const snapshot = new RuleValueSnapshot()
    valueBrand.add(snapshot)
    valueData.set(snapshot, parsed)
    return snapshot
  }
}

/** One JSON-text-captured child-table row accepted by graph mutation. */
export class RuleRowSnapshot {
  private constructor() {}

  static fromJsonText(jsonText: string): RuleRowSnapshot {
    const parsed: unknown = parseBoundedJsonText(jsonText, 'rule row')
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) throw new TypeError('rule row must be a JSON object')
    const value = parsed as Record<string, Json>
    if (typeof value.id !== 'string' || value.fields === null || typeof value.fields !== 'object' || Array.isArray(value.fields))
      throw new TypeError('rule row must contain id and fields')
    const snapshot = new RuleRowSnapshot()
    rowBrand.add(snapshot)
    rowData.set(snapshot, { id: value.id, fields: cloneRecord(value.fields as Record<string, Json>), hasExplicitId: true })
    return snapshot
  }
}

/**
 * A form instance the rule graph evaluates over (SPINE-1 §2.1) — TS mirror of the
 * .NET `RuleInstance`. A property whose value is a non-empty array of objects is a
 * child table (section id = property name; row id = each element's `_id`, else index).
 */
export class RuleInstance {
  private constructor() {}

  /** Refuses arbitrary objects without reflecting over them; use the JSON-text host capture. */
  static fromJson(_json: unknown): never {
    throw new Error(Codes.contextSnapshotRequired)
  }

  /** Parses and owns an inert instance before it crosses into graph evaluation. */
  static fromJsonText(jsonText: string): RuleInstance {
    const json: unknown = parseBoundedJsonText(jsonText, 'rule instance')
    if (json === null || typeof json !== 'object' || Array.isArray(json)) throw new TypeError('rule instance must be a JSON object')
    const fields = emptyRecord<Json>()
    const tables = emptyRecord<RuleRow[]>()
    for (const [key, value] of Object.entries(json as Record<string, Json>)) {
      if (Array.isArray(value) && value.length > 0 && value.every((e) => typeof e === 'object' && e !== null && !Array.isArray(e))) {
        const rows: RuleRow[] = value.map((el, i) => {
          const rowObj = el as Record<string, Json>
          const id = typeof rowObj['_id'] === 'string' ? rowObj['_id'] : String(i)
          const fields = emptyRecord<Json>()
          for (const [fk, fv] of Object.entries(rowObj)) {
            if (fk === '_id') continue
            fields[fk] = fv
          }
          return { id, fields, hasExplicitId: Object.hasOwn(rowObj, '_id') }
        })
        tables[key] = rows
      } else {
        fields[key] = value
      }
    }
    const instance = new RuleInstance()
    instanceBrand.add(instance)
    instanceData.set(instance, { fields, tables })
    return instance
  }

  /** @internal */
  static isRuntimeOwned(value: unknown): value is RuleInstance { return typeof value === 'object' && value !== null && instanceBrand.has(value) }

  /** @internal */
  static empty(): RuleInstance {
    const instance = new RuleInstance()
    instanceBrand.add(instance)
    instanceData.set(instance, { fields: emptyRecord<Json>(), tables: emptyRecord<RuleRow[]>() })
    return instance
  }

  /** @internal Creates graph-local owned state without touching caller-visible properties. */
  static cloneOwned(value: RuleInstance): RuleInstance {
    const data = ownedInstanceDataOf(value)
    if (!data) throw new Error(Codes.contextSnapshotRequired)
    const instance = new RuleInstance()
    instanceBrand.add(instance)
    instanceData.set(instance, {
      fields: cloneRecord(data.fields),
      tables: cloneTables(data.tables),
    })
    return instance
  }
}

/** Creates a caller-owned JSON copy; evaluator state never crosses this boundary by alias. */
export function detachJson(value: Json): Json {
  return freezeJson(cloneJson(value))
}

function cloneJson(value: Json): Json {
  if (Array.isArray(value)) return value.map(cloneJson)
  if (value !== null && typeof value === 'object') return cloneRecord(value as Record<string, Json>)
  return value
}

function cloneRecord(source: Record<string, Json>): Record<string, Json> {
  const target = emptyRecord<Json>()
  for (const [key, value] of Object.entries(source)) target[key] = cloneJson(value)
  return target
}

function cloneTables(source: Record<string, RuleRow[]>): Record<string, RuleRow[]> {
  const target = emptyRecord<RuleRow[]>()
  for (const [section, rows] of Object.entries(source))
    target[section] = rows.map((row) => ({ id: row.id, fields: cloneRecord(row.fields), hasExplicitId: row.hasExplicitId }))
  return target
}

function emptyRecord<T>(): Record<string, T> {
  return Object.create(null) as Record<string, T>
}

function freezeJson(value: Json): Json {
  if (Array.isArray(value)) { for (const child of value) freezeJson(child); return Object.freeze(value) as unknown as Json }
  if (value !== null && typeof value === 'object') { for (const child of Object.values(value)) freezeJson(child); return Object.freeze(value) as Json }
  return value
}

/** Internal graph-only accessors. They are deliberately not re-exported by the package entrypoint. */
export function ownedInstanceDataOf(value: unknown): { fields: Record<string, Json>, tables: Record<string, RuleRow[]> } | undefined {
  return typeof value === 'object' && value !== null && instanceBrand.has(value) ? instanceData.get(value) : undefined
}
export function ownedValueOf(value: unknown): Json | undefined {
  return typeof value === 'object' && value !== null && valueBrand.has(value) ? valueData.get(value) : undefined
}
export function ownedRowOf(value: unknown): RuleRow | undefined {
  return typeof value === 'object' && value !== null && rowBrand.has(value) ? rowData.get(value) : undefined
}
