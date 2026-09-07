import { describe, expect, it } from 'vitest'

import { CompileError } from '../grammar.js'
import type { FormulaSkin } from './formula.js'
import { compileFormula } from './formula.js'
import { SkinCodes } from './codes.js'

function formula(overrides: Partial<FormulaSkin> = {}): FormulaSkin {
  return {
    ruleId: 'formula.total',
    scope: 'Field',
    scopeTarget: 'total',
    action: 'Compute',
    inputs: [],
    expression: 0,
    ...overrides,
  }
}

function compileError(run: () => unknown): CompileError {
  try {
    run()
  } catch (error) {
    expect(error).toBeInstanceOf(CompileError)
    return error as CompileError
  }
  throw new Error('expected compileFormula to throw')
}

describe('compileFormula', () => {
  it('lowers a boundary constant expression with no declared inputs', () => {
    const skin = formula()

    expect(compileFormula(skin)).toEqual({
      id: 'formula.total',
      tier: 'JsonLogic',
      scope: 'Field',
      scopeTarget: 'total',
      expression: 0,
      action: 'Compute',
    })
  })

  it('accepts declared references, including a reference in a var default', () => {
    const expression = {
      '+': [
        { var: 'amount' },
        { var: ['tax', { var: 'fallbackTax' }] },
        { var: 'amount' },
      ],
    }

    expect(compileFormula(formula({
      inputs: [{ ref: 'amount' }, { ref: 'tax' }, { ref: 'fallbackTax' }],
      expression,
    })).expression).toBe(expression)
  })

  it('rejects an empty expression with the stable formula-empty code', () => {
    const error = compileError(() => compileFormula(formula({ expression: null })))

    expect(error).toMatchObject({ code: SkinCodes.formulaEmpty, ruleId: 'formula.total' })
  })

  it('rejects an undeclared variable reference with the stable code', () => {
    const error = compileError(() => compileFormula(formula({ expression: { var: 'missing' } })))

    expect(error).toMatchObject({ code: SkinCodes.formulaUndeclaredRef, ruleId: 'formula.total' })
  })
})
