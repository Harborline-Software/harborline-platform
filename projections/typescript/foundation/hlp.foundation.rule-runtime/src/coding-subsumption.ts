import { parseBoundedJsonText } from './input-envelope.js'
import { type EvalBudget, RuleEvalError, err } from './eval-support.js'
import { Codes } from './codes.js'
import type { Json } from './model.js'

type Binding = readonly [string, string, string, string]
type Answer = readonly [string, string, string, boolean]
const owned = new WeakMap<object, { bindings: readonly Binding[], answers: readonly Answer[] }>()

/** Finite Taxonomy answers joined to independently supplied host tenant/version/hash pins. */
export class CodingSubsumptionSnapshot {
  private constructor() {}

  static fromJsonText(answersJsonText: string, expectedBindingsJsonText: string): CodingSubsumptionSnapshot {
    if (typeof answersJsonText !== 'string' || typeof expectedBindingsJsonText !== 'string') throw invalid()
    const expected = tuple(captureDocument(expectedBindingsJsonText, 'coding bindings'), 3)
    const actual = tuple(captureDocument(answersJsonText, 'coding answers'), 4)
    if (text(expected[0]) !== 'harborline-taxonomy-coding-bindings/1'
      || text(actual[0]) !== 'harborline-taxonomy-coding-answers/1'
      || text(expected[1]) !== text(actual[1])) throw invalid()
    const pins = bindings(expected[2])
    const supplied = bindings(actual[2])
    if (pins.length !== supplied.length || pins.some((row, i) => row.some((part, j) => part !== supplied[i][j]))) throw invalid()
    if (!Array.isArray(actual[3]) || actual[3].length === 0) throw invalid()
    const answers: Answer[] = []
    for (const item of actual[3]) {
      const row = tuple(item, 4)
      const system = text(row[0]), ancestor = text(row[1]), descendant = text(row[2])
      if (typeof row[3] !== 'boolean' || !pins.some(pin => pin[0] === system)) throw invalid()
      const answer: Answer = [system, ancestor, descendant, row[3]]
      if (answers.length > 0 && compare(answers[answers.length - 1], answer) >= 0) throw invalid()
      answers.push(answer)
    }
    const snapshot = new CodingSubsumptionSnapshot()
    owned.set(snapshot, { bindings: pins, answers })
    return Object.freeze(snapshot)
  }
}

/** @internal Reject forged objects without reading caller properties or invoking callbacks. */
export function requireCodingSnapshot(value: unknown): asserts value is CodingSubsumptionSnapshot {
  if (typeof value !== 'object' || value === null || !owned.has(value)) throw invalid()
}

/** @internal */
export function codingEqual(left: string, right: string, budget: EvalBudget): boolean {
  budget.chargeSize(left.length + right.length + 1)
  return left === right
}

/** @internal */
export function requireCodingBinding(snapshot: CodingSubsumptionSnapshot, system: string, budget: EvalBudget): void {
  const data = owned.get(snapshot)
  if (!data) throw invalid()
  for (const binding of data.bindings) {
    budget.charge()
    if (codingEqual(binding[0], system, budget)) return
  }
  throw new RuleEvalError(err(Codes.codingBindingMissing))
}

/** @internal */
export function codingAnswer(snapshot: CodingSubsumptionSnapshot, system: string, ancestor: string, descendant: string, budget: EvalBudget): boolean {
  const data = owned.get(snapshot)
  if (!data) throw invalid()
  for (const answer of data.answers) {
    budget.charge()
    if (codingEqual(answer[0], system, budget) && codingEqual(answer[1], ancestor, budget)
      && codingEqual(answer[2], descendant, budget)) return answer[3]
  }
  throw new RuleEvalError(err(Codes.codingAnswerMissing))
}

function captureDocument(text: string, label: string): Json {
  try { return parseBoundedJsonText(text, label) }
  catch (error) {
    if (error instanceof RangeError) throw new TypeError(error.message, { cause: error })
    throw error
  }
}
function tuple(value: Json | undefined, count: number): Json[] {
  if (!Array.isArray(value) || value.length !== count) throw invalid()
  return value
}
function text(value: Json | undefined): string {
  if (typeof value !== 'string' || value.length === 0) throw invalid()
  return value
}
function bindings(value: Json | undefined): Binding[] {
  if (!Array.isArray(value) || value.length === 0) throw invalid()
  const result: Binding[] = []
  for (const item of value) {
    const row = tuple(item, 4)
    const binding: Binding = [text(row[0]), text(row[1]), text(row[2]), text(row[3])]
    const id = binding[1].split('.'), version = binding[2].split('.')
    if (id.length !== 3 || id.some(part => part.length === 0) || version.length !== 3
      || version.some(part => !uint32(part))
      || !/^[0-9a-f]{64}$/.test(binding[3])
      || result.length > 0 && result[result.length - 1][0] >= binding[0]) throw invalid()
    result.push(binding)
  }
  return result
}
function compare(left: Answer, right: Answer): number {
  for (const index of [0, 1, 2] as const) {
    if (left[index] < right[index]) return -1
    if (left[index] > right[index]) return 1
  }
  return 0
}
function uint32(part: string): boolean {
  if (!/^[0-9]+$/.test(part)) return false
  const digits = part.replace(/^0+/, '')
  return digits.length < 10 || digits.length === 10 && digits <= '4294967295'
}
function invalid(): TypeError { return new TypeError('Invalid pinned Taxonomy coding capture') }
