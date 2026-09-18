import { DEFAULT_LIMITS, type Json } from '@harborline-software/rule-engine'

export class DefinitionReadError extends Error {
  constructor(readonly code: string, readonly location: string) { super(code) }
}

export function pointer(parent: string, member: string | number): string {
  return `${parent}/${String(member).replace(/~/g, '~0').replace(/\//g, '~1')}`
}

/** JSON.parse owns syntax. This bounded token walk checks every member before returning its value. */
export function readDefinitionJson(json: string): Json {
  const parsed: Json = JSON.parse(json)
  let offset = 0
  let duplicate: string | undefined
  const whitespace = () => { while (/[\x20\t\r\n]/.test(json[offset] ?? '\0')) offset++ }
  const string = (): string => {
    const start = offset++
    while (offset < json.length) {
      const char = json[offset++]
      if (char === '\\') offset++
      else if (char === '"') return JSON.parse(json.slice(start, offset)) as string
    }
    throw new SyntaxError('Unterminated string')
  }
  const walk = (location: string, depth: number): void => {
    whitespace()
    const char = json[offset]
    if (char === '{' || char === '[') {
      if (depth >= DEFAULT_LIMITS.maxAstNodes * 4) throw new SyntaxError('JSON depth exceeded')
      const object = char === '{'
      const end = object ? '}' : ']'
      const names = new Set<string>()
      offset++
      whitespace()
      let index = 0
      if (json[offset] !== end) {
        while (true) {
          whitespace()
          const member = object ? string() : index++
          const child = pointer(location, member)
          if (object) {
            if (names.has(String(member)) && duplicate === undefined) duplicate = child
            names.add(String(member))
            whitespace()
            offset++ // colon; syntax has already been checked
          }
          walk(child, depth + 1)
          whitespace()
          if (json[offset] !== ',') break
          offset++
        }
      }
      offset++
    } else if (char === '"') {
      string()
    } else {
      while (offset < json.length && !/[\x20\t\r\n,\]}]/.test(json[offset])) offset++
    }
  }
  walk('', 0)
  if (duplicate !== undefined) throw new DefinitionReadError('rules.definition.duplicate_member', duplicate)
  return parsed
}
