/**
 * Borrower environment admission (DES-0018 rules-ck-28 and rules-eng-26), TS reactive tier. Mirrors
 * the .NET `BorrowerEnvironmentAdmission`: a borrower's typed declaration is admitted here, only this
 * module mints the admission evidence, and every evaluation entry point refuses without it, for an
 * inapplicable phase, or for a program using an operation or scope token the declaration does not lend.
 */
import { registeredBuiltIn } from './jsonlogic.js'
import type { Json } from './model.js'

export type EvaluationPhase = 'AuthoringValidation' | 'PublishValidation' | 'Render' | 'Submission' | 'Run' | 'SignOff'
export const evaluationPhases: readonly EvaluationPhase[] = ['AuthoringValidation', 'PublishValidation', 'Render', 'Submission', 'Run', 'SignOff']

/** ADR 0099 decision 8: every member explicit, every phase marked applicable or not. */
export interface BorrowerEnvironmentDeclaration {
  readonly borrower: string
  readonly grammar: string
  readonly variables: Readonly<Record<string, string>>
  readonly operations: readonly string[]
  readonly effects: readonly string[]
  readonly missingValues: string
  readonly timeSource: string
  readonly timeZone: string
  readonly phases: Readonly<Record<EvaluationPhase, boolean>>
  readonly replay: string
}

export const BorrowerEnvironmentCodes = {
  declarationRefused: 'rule.environment.declaration_refused',
  notAdmitted: 'rule.environment.not_admitted',
  phaseNotAdmitted: 'rule.environment.phase_not_admitted',
  operationNotAdmitted: 'rule.environment.operation_not_admitted',
  variableNotAdmitted: 'rule.environment.variable_not_admitted',
} as const

export const lentGrammar = 'harborline-jsonlogic/v1'
export const fieldReadEffect = 'field-read'

export class BorrowerEnvironmentError extends Error {
  constructor(readonly code: string, message: string) { super(message) }
}

/** Evidence for one evaluation: an admitted declaration bound to a phase. Only `admitEnvironment` mints it. */
export interface EvaluationAdmission { readonly declaration: BorrowerEnvironmentDeclaration; readonly phase: EvaluationPhase }
/** An admitted declaration; `forPhase` yields the evidence an entry point requires. */
export interface AdmittedEnvironment { readonly declaration: BorrowerEnvironmentDeclaration; forPhase(phase: EvaluationPhase): EvaluationAdmission }

const minted = new WeakSet<object>()
const blank = (value: unknown) => typeof value !== 'string' || value.trim().length === 0

export function admitEnvironment(declaration: BorrowerEnvironmentDeclaration): AdmittedEnvironment {
  const d = declaration
  const reason =
    blank(d.borrower) ? 'borrower'
      : d.grammar !== lentGrammar ? 'grammar'
        : Object.keys(d.variables ?? {}).length === 0 || Object.entries(d.variables).some(([k, v]) => blank(k) || blank(v)) ? 'variables'
          : !Array.isArray(d.operations) || d.operations.some((op) => registeredBuiltIn(op) === undefined) ? 'operations'
            : !Array.isArray(d.effects) || d.effects.some((effect) => effect !== fieldReadEffect) ? 'effects'
              : blank(d.missingValues) ? 'missing-values'
                : blank(d.timeSource) || blank(d.timeZone) ? 'time'
                  : evaluationPhases.some((phase) => typeof d.phases?.[phase] !== 'boolean') ? 'phases'
                    : blank(d.replay) ? 'replay' : null
  if (reason !== null) throw new BorrowerEnvironmentError(BorrowerEnvironmentCodes.declarationRefused, `${d.borrower}: ${reason}`)
  const frozen = Object.freeze({ ...d })
  return Object.freeze({
    declaration: frozen,
    forPhase(phase: EvaluationPhase): EvaluationAdmission {
      if (frozen.phases[phase] !== true) throw new BorrowerEnvironmentError(BorrowerEnvironmentCodes.phaseNotAdmitted, `${frozen.borrower} does not admit evaluation in phase ${phase}`)
      const admission = Object.freeze({ declaration: frozen, phase })
      minted.add(admission)
      return admission
    },
  })
}

/** @internal The runtime check at an entry point: null when admitted, otherwise the refusal code. */
export function admissionRefusal(admission: EvaluationAdmission | null | undefined, programs: readonly Json[]): string | null {
  if (admission === null || admission === undefined || !minted.has(admission)) return BorrowerEnvironmentCodes.notAdmitted
  const operations = new Set(admission.declaration.operations)
  const variables = new Set(Object.keys(admission.declaration.variables))
  const walk = (node: Json): string | null => {
    if (typeof node !== 'object' || node === null || Array.isArray(node)) return null
    const keys = Object.keys(node)
    if (keys.length !== 1) return null
    const op = keys[0]
    if (!operations.has(op)) return BorrowerEnvironmentCodes.operationNotAdmitted
    const raw = (node as Record<string, Json>)[op]
    const args = Array.isArray(raw) ? raw : [raw]
    if (op === 'var' && typeof args[0] === 'string') {
      const dot = args[0].indexOf('.')
      if (!variables.has(dot < 0 ? 'field' : args[0].slice(0, dot))) return BorrowerEnvironmentCodes.variableNotAdmitted
    }
    if (op === 'agg' && !variables.has('row')) return BorrowerEnvironmentCodes.variableNotAdmitted
    for (const arg of args) { const refusal = walk(arg); if (refusal !== null) return refusal }
    return null
  }
  for (const program of programs) { const refusal = walk(program); if (refusal !== null) return refusal }
  return null
}
