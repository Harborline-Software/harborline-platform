/**
 * Compositional finite-work proof for the closed evaluator.  This is deliberately
 * a compiler proof, not a second interpreter or a fuel counter: it follows the
 * same executable-node boundary as core-types and assigns a finite transfer bound
 * to every operator arm.  The graph combines the transfers with its checked DAG,
 * finite dynamic-demand depth, bounded row expansion, and captured-input envelope.
 */
import { deriveCoreTypes } from './core-types.js'
import type { RuleEngineLimits } from './limits.js'
import type { Json, RuleDefinition } from './model.js'

const INPUT_BYTES = 262_144n
const INPUT_NODES = 5_000n
const MONEY_DIGITS = 4_096n
const MONEY_SCALE = 4_096n
const NUMBER_BYTES = 32n
// Two parsed operands can each carry 4096 digits.  Alignment can append at
// most 4096 zeroes to one mantissa; a sum/subtract can carry one more digit.
// Canonical decimal text additionally has sign, point and JSON quotes.
const MONEY_ADD_RESULT_BYTES = (MONEY_DIGITS + MONEY_SCALE + 1n) + 4n
const MONEY_MUL_RESULT_BYTES = MONEY_DIGITS + 4n

export interface WorkProof {
  /** Largest serialized result admitted by the proof; metadata only, never authored JSON. */
  maximumResultBytes: bigint
  /** Structural copying/conversion/comparison/fold work for one bounded graph generation. */
  maximumEvaluationWork: bigint
}

interface NodeProof {
  result: bigint
  work: bigint
  reads: bigint
  aggregateReads: bigint
}

const max = (...values: bigint[]): bigint => values.reduce((a, b) => a > b ? a : b, 0n)
const sum = (values: readonly bigint[]): bigint => values.reduce((a, b) => a + b, 0n)
const add = (...values: bigint[]): bigint => sum(values)
const isObject = (value: Json): value is Record<string, Json> => value !== null && typeof value === 'object' && !Array.isArray(value)

// JSON text can escape each UTF-16 code unit as \uXXXX.  This deliberately
// over-bounds UTF-8/escaping without allocating a second serialized expression.
function literalBytes(value: Json): bigint {
  if (value === null) return 4n
  if (typeof value === 'boolean') return value ? 4n : 5n
  if (typeof value === 'number') return NUMBER_BYTES
  if (typeof value === 'string') return 2n + 6n * BigInt(value.length)
  if (Array.isArray(value)) return 2n + sum(value.map(literalBytes)) + BigInt(Math.max(0, value.length - 1))
  return 2n + sum(Object.entries(value).map(([key, child]) => 2n + 6n * BigInt(key.length) + 1n + literalBytes(child)))
    + BigInt(Math.max(0, Object.keys(value).length - 1))
}

function inertProof(value: Json): NodeProof {
  const bytes = literalBytes(value)
  return { result: bytes, work: add(1n, bytes), reads: 0n, aggregateReads: 0n }
}

function merge(children: readonly NodeProof[]): Pick<NodeProof, 'work' | 'reads' | 'aggregateReads'> {
  return {
    work: sum(children.map(child => child.work)),
    reads: sum(children.map(child => child.reads)),
    aggregateReads: sum(children.map(child => child.aggregateReads)),
  }
}

// `cat` converts JSON values to text and returns a JSON string.  A source JSON
// byte can become a UTF-16 code unit and be escaped again (six bytes), plus the
// enclosing quotes.  This covers nested literal containers and quoted strings.
function catStringResultBytes(value: bigint): bigint { return 2n + 6n * value }

function staticVarPath(raw: Json): string | null {
  const path = Array.isArray(raw) ? raw[0] : raw
  return typeof path === 'string' ? path : null
}

function deriveNode(node: Json, dependencyResult: bigint, aggregateResult: bigint,
  staticReference?: (path: string) => bigint | undefined): NodeProof {
  // The evaluator regards arrays and multi-key objects as literal JSON data.
  if (!isObject(node) || Object.keys(node).length !== 1) return inertProof(node)
  const [op, raw] = Object.entries(node)[0]
  const args = Array.isArray(raw) ? raw : [raw]
  const children = args.map(arg => deriveNode(arg, dependencyResult, aggregateResult, staticReference))
  const child = merge(children)
  const childResult = max(...children.map(item => item.result))
  const childBytes = sum(children.map(item => item.result))
  const base = (result: bigint, transfer: bigint, reads = child.reads, aggregateReads = child.aggregateReads): NodeProof => ({
    result,
    work: add(1n, child.work, transfer),
    reads,
    aggregateReads,
  })

  switch (op) {
    case 'var':
      // The fallback is eager in both tiers.  The resolver lookup is explicit work.
      { const path = staticVarPath(raw)
        const resolved = path === null ? dependencyResult : staticReference?.(path) ?? INPUT_BYTES
        return base(max(resolved, childResult), add(childBytes, 1n), child.reads + 1n) }
    case 'missing': {
      // One non-array operand is evaluated once for the array test and again to make
      // the key list.  Every discovered key performs a resolver lookup.
      const keyCount = args.length === 1 ? INPUT_NODES : BigInt(args.length)
      const firstTwice = args.length === 1 ? children[0]?.work ?? 0n : 0n
      const keyBytes = max(childResult, 1n)
      return {
        result: add(2n, keyCount * add(keyBytes, 1n)),
        work: add(1n, child.work, firstTwice, keyCount * add(keyBytes, 1n)),
        reads: child.reads + (args.length === 1 ? children[0]?.reads ?? 0n : 0n) + keyCount,
        aggregateReads: child.aggregateReads + (args.length === 1 ? children[0]?.aggregateReads ?? 0n : 0n),
      }
    }
    case 'missing_some': {
      const keyBytes = max(children[1]?.result ?? 0n, 1n)
      return base(add(2n, INPUT_NODES * add(keyBytes, 1n)), add(childBytes, INPUT_NODES * add(keyBytes, 1n)), child.reads + INPUT_NODES)
    }
    case '==': case '!=': case '===': case '!==':
      // Object/array equality serializes complete operands in both tiers.
      return base(5n, childBytes)
    case '!': case '!!': case '>': case '>=': case '<': case '<=':
      return base(5n, childBytes)
    case 'and': case 'or': case 'if':
      // A branch short-circuits at runtime; evaluating every executable child is a
      // conservative transfer upper bound, while its result is one selected child/null.
      return base(max(5n, childResult), 0n)
    case '+': case '-': case '*': case '/': case '%': case 'min': case 'max':
      return base(NUMBER_BYTES, childBytes)
    case 'in':
      // Array membership can compare every member; loose equality can serialize both sides.
      return base(5n, INPUT_NODES * add(childBytes, 1n))
    case 'cat': {
      const output = args.length === 0 ? 2n : sum(children.map(item => catStringResultBytes(item.result)))
      // Repeated concatenation copies growing prefixes even where a host uses ropes.
      return base(output, BigInt(args.length) * output)
    }
    case 'agg':
      return base(aggregateResult, childBytes + 1n, child.reads + 1n, child.aggregateReads + 1n)
    case 'money.add': case 'money.sub': case 'money.mul': {
      // Parse scans the whole captured operand before its 4096-digit refusal; successful
      // decimal alignment/multiply is bounded by the existing 4096 digit/scale contract.
      const count = BigInt(args.length)
      // The evaluator folds every admitted operand. Each add/sub can align against
      // the accumulated mantissa and carry once; every operand is parsed/copy-charged.
      const result = op === 'money.mul' ? MONEY_MUL_RESULT_BYTES : MONEY_DIGITS + MONEY_SCALE + count + 4n
      const decimalWork = childBytes + count * MONEY_DIGITS * MONEY_DIGITS + count * result
      return base(result, decimalWork)
    }
    case 'date.add': return base(12n, childBytes)
    case 'date.diff': return base(NUMBER_BYTES, childBytes)
    case 'date.today': return base(12n, 0n)
    case 'coding.is':
      return base(5n, INPUT_NODES * add(childBytes, 1n))
    default:
      // RuleCompiler.ValidateOperators has already closed the language.  Keeping this
      // refusal here makes a newly added evaluator arm fail proof admission until it has
      // an explicit transfer rule, rather than silently receiving a generic estimate.
      throw new Error(`work proof has no transfer for '${op}'`)
  }
}

/** Derives one executable expression's transfer proof through the shared core-type walker. */
export function deriveCoreWork(node: Json, ruleId: string, dependencyResult = INPUT_BYTES,
  staticReference?: (path: string) => bigint | undefined, aggregateResult = INPUT_BYTES): NodeProof {
  // This call deliberately shares the type derivation's executable/literal boundary.
  deriveCoreTypes(node, ruleId)
  return deriveNode(node, dependencyResult, aggregateResult, staticReference)
}

function repeatedCellEnvelope(seed: bigint, factor: bigint, addend: bigint, cells: number): bigint {
  const steps = BigInt(Math.max(0, cells))
  if (steps === 0n) return seed
  if (factor === 0n) return addend
  if (factor === 1n) return seed + steps * addend
  const power = factor ** steps
  return power * seed + addend * ((power - 1n) / (factor - 1n))
}

/**
 * Combines actual admitted rules with finite runtime dimensions.  Dynamic paths are
 * not claimed to be static edges: each resolver lookup is charged, and a dynamic chain
 * is unrolled only through the scheduler's validated depth bound.
 */
export function deriveGraphWork(rules: readonly { source: RuleDefinition, ast: Json, references?: readonly { kind: string }[] }[], limits: RuleEngineLimits): WorkProof {
  const aggregateResult = BigInt(Math.max(0, limits.maxTableRowsPerAggregate)) * MONEY_ADD_RESULT_BYTES
  let dynamicResult = INPUT_BYTES
  let perRule: NodeProof[] = []
  let staticResults = new Map<string, bigint>()
  // Static dependencies are already a checked DAG. Reach a fixed point of this
  // admitted program, rather than using a separately configurable host depth.
  for (let pass = 0; pass <= rules.length; pass++) {
    perRule = rules.map(rule => deriveCoreWork(rule.ast, rule.source.id, INPUT_BYTES,
      path => staticResults.get(path), aggregateResult))
    const next = new Map(staticResults)
    for (let index = 0; index < rules.length; index++) {
      const rule = rules[index]
      if (rule.source.action !== 'Compute') continue
      if (rule.source.scope === 'Field') next.set(`field.${rule.source.scopeTarget}`, perRule[index].result)
      if (rule.source.scope === 'Row') {
        const slash = rule.source.scopeTarget.indexOf('/')
        if (slash >= 0) {
          const key = `row.${rule.source.scopeTarget.slice(slash + 1)}`
          next.set(key, max(next.get(key) ?? 0n, perRule[index].result))
        }
      }
    }
    dynamicResult = max(INPUT_BYTES, aggregateResult, ...perRule.map(proof => proof.result))
    const unchanged = next.size === staticResults.size && [...next].every(([key, value]) => staticResults.get(key) === value)
    staticResults = next
    if (unchanged) break
  }

  // A successful dynamic demand chain has no repeated cell: the scheduler refuses an
  // active cycle and retains completed cells only for this generation.  Its semantic
  // height is therefore bounded by the validated cell inventory, not the active-stack
  // depth.  Every closed transfer either selects a value, produces a fixed scalar, or
  // serializes at most AST/input-node children; the latter is at most this affine map.
  // Exponentiation composes it in log(cell-count) proof work, rather than re-walking a
  // dependency-free expression once per possible cell.
  const hasDynamicRead = rules.some(rule => rule.references?.some(reference => reference.kind === 'dynamic-read'))
  if (hasDynamicRead) {
    const childrenPerCell = BigInt(Math.max(limits.maxAstNodes, Number(INPUT_NODES)))
    dynamicResult = repeatedCellEnvelope(dynamicResult, 6n * childrenPerCell, aggregateResult,
      Math.max(0, limits.maxGraphNodes))
  }

  let localWork = 0n
  let resolverReads = 0n
  let aggregateReads = 0n
  let maxCellWork = 1n
  for (let index = 0; index < rules.length; index++) {
    const rule = rules[index]
    const proof = perRule[index]
    const rowExpansion = rule.source.scope === 'Row' ? BigInt(Math.max(0, limits.maxTableRowsPerAggregate)) : 1n
    // Every rule produces an outcome; Compute rules additionally produce a value cell.
    const executions = rowExpansion * (rule.source.action === 'Compute' ? 2n : 1n)
    // Includes outcome/value ownership copies after evaluator execution.
    localWork += executions * add(proof.work, proof.result)
    resolverReads += executions * proof.reads
    aggregateReads += executions * proof.aggregateReads
    if (proof.work > maxCellWork) maxCellWork = proof.work
  }

  if (hasDynamicRead) {
    // A dynamically selected value can be converted, serialized and copied by every
    // executable operand position.  The same finite child factor used for result
    // composition therefore bounds one demanded cell's conversion/copy work.
    const childFactor = 6n * BigInt(Math.max(limits.maxAstNodes, Number(INPUT_NODES)))
    maxCellWork = max(maxCellWork, childFactor * add(dynamicResult, aggregateResult, INPUT_BYTES))
  }

  // Actual dynamic reads can demand a completed graph cell.  The current generation
  // completes each cell once; repeated resolver calls still pay lookup/demand work.
  const dynamicDemand = resolverReads * add(1n, BigInt(Math.max(0, limits.maxGraphNodes)) * maxCellWork)
  // Each aggregate fold visits at most the admitted rows and converts/copies a bounded
  // captured row value.  This is separate from the evaluator's agg resolver arm.
  const foldWork = aggregateReads * BigInt(Math.max(0, limits.maxTableRowsPerAggregate))
    * add(max(dynamicResult, aggregateResult, INPUT_BYTES), 1n)
  return Object.freeze({
    maximumResultBytes: dynamicResult,
    maximumEvaluationWork: add(localWork, dynamicDemand, foldWork),
  })
}
