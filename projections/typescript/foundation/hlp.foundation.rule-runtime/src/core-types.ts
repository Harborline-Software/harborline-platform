import type { Json } from './model.js'
import { CompileError } from './grammar.js'
import { Codes } from './codes.js'

/** Finite abstract JSON domain mirrored by the native core admission fence. */
export type CoreJsonType = 'null' | 'boolean' | 'number' | 'string' | 'array' | 'object'
export interface CoreTypeResult { types: ReadonlySet<CoreJsonType>; canError: boolean; canPending: boolean }
const anyJson = new Set<CoreJsonType>(['null', 'boolean', 'number', 'string', 'array', 'object'])
const scalar = new Set<CoreJsonType>(['null', 'boolean', 'number', 'string'])

export function declaredCoreType(type: string | undefined, ruleId: string, code: string = Codes.compileInvalidExpression): ReadonlySet<CoreJsonType> {
  switch ((type ?? 'any').toLowerCase()) {
    case 'any': return anyJson
    case 'null': return new Set(['null'])
    case 'boolean': return new Set(['boolean'])
    case 'number': return new Set(['number'])
    case 'string': case 'text': return new Set(['string'])
    case 'money': case 'date': return new Set(['string'])
    case 'array': return new Set(['array'])
    case 'object': case 'coding': return new Set(['object'])
    default: throw new CompileError(code, `rule '${ruleId}': input declares unknown type '${type}'`, ruleId)
  }
}

/** Visit every executable child and derive finite result/error/pending possibilities. */
export function deriveCoreTypes(node: Json, ruleId: string,
  input: (path: string) => ReadonlySet<CoreJsonType> = () => anyJson,
  code: string = Codes.compileInvalidExpression): CoreTypeResult {
  const visit = (value: Json): CoreTypeResult => {
    if (value === null) return result(['null'])
    if (Array.isArray(value)) return result(['array'])
    if (typeof value !== 'object') return result([typeof value as CoreJsonType])
    const entries = Object.entries(value)
    if (entries.length !== 1) return result(['object'])
    const [op, raw] = entries[0]
    const args = Array.isArray(raw) ? raw : [raw]
    const children = args.map(visit)
    const join = (types: Iterable<CoreJsonType>, canError = false, canPending = false): CoreTypeResult => ({
      types: new Set(types), canError: canError || children.some(c => c.canError), canPending: canPending || children.some(c => c.canPending),
    })
    const require = (accepted: ReadonlySet<CoreJsonType>, positions: number[]): void => {
      if (positions.some(index => index < children.length && ![...children[index].types].some(type => accepted.has(type)))) {
        throw new CompileError(code, `rule '${ruleId}': operator '${op}' cannot coerce a closed container operand.`, ruleId)
      }
    }
    switch (op) {
      case 'var': {
        const path = varPath(args[0])
        const primary = input(path)
        return join(union(primary, children[1]?.types ?? (sameTypes(primary, anyJson) ? ['null'] : [])), true, true)
      }
      case 'missing': case 'missing_some': return join(['array'], true, true)
      case '==': case '!=': case '===': case '!==': case '!': case '!!': case 'in': case 'coding.is': return join(['boolean'])
      case '>': case '>=': case '<': case '<=': return join(['boolean'], true)
      case 'and': case 'or': return join(children.length === 0 ? ['boolean'] : union(...children.map(c => c.types)))
      case 'if': {
        // JsonLogic returns null when no condition matches and no trailing default is
        // supplied.  That includes the empty form and every even argument count.
        const types = new Set<CoreJsonType>(['null'])
        for (let index = 1; index < children.length; index += 2) {
          for (const type of children[index].types) types.add(type)
        }
        if (children.length % 2 === 1) {
          for (const type of children[children.length - 1].types) types.add(type)
        }
        return join(types)
      }
      case 'cat': return join(['string'])
      case '+': case '-': case '*': case '/': case '%': case 'min': case 'max': require(scalar, children.map((_, i) => i)); return join(['number'], true)
      case 'money.add': case 'money.sub': case 'money.mul': require(new Set(['number', 'string']), children.map((_, i) => i)); return join(['string'], true)
      case 'date.add': case 'date.diff':
        require(new Set(['string']), [0])
        if (op === 'date.add') { require(scalar, [1]); require(new Set(['string']), [2]) } else require(new Set(['string']), [1])
        return join([op === 'date.diff' ? 'number' : 'string'], true)
      case 'date.today': return result(['string'])
      case 'agg': return join(anyJson, true, true)
      default: return join(anyJson, true, true)
    }
  }
  return visit(node)
}
function result(types: Iterable<CoreJsonType>): CoreTypeResult { return { types: new Set(types), canError: false, canPending: false } }
function union<T>(...items: Iterable<T>[]): Set<T> { return new Set(items.flatMap(item => [...item])) }
function sameTypes<T>(a: ReadonlySet<T>, b: ReadonlySet<T>): boolean { return a.size === b.size && [...a].every(item => b.has(item)) }
function varPath(value: Json | undefined): string { return Array.isArray(value) ? typeof value[0] === 'string' ? value[0] : '' : typeof value === 'string' ? value : '' }
