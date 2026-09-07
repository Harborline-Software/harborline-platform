import { describe, expect, it } from 'vitest'

import { err, refError, refPending, refResolved } from './eval-support.js'

describe('reference values', () => {
  it('represents resolved values, including an undefined JSON value', () => {
    expect(refResolved('ready')).toEqual({ state: 'Resolved', value: 'ready' })
    expect(refResolved(undefined)).toEqual({ state: 'Resolved', value: undefined })
  })

  it('preserves errors and exposes the shared pending singleton', () => {
    const error = err('rule.invalid', 'field', 'amount')

    expect(refError(error)).toEqual({ state: 'Error', error })
    expect(refPending).toEqual({ state: 'Pending' })
  })
})

describe('err', () => {
  it('creates an empty parameter map unless given both a key and value', () => {
    expect(err('rule.invalid')).toEqual({ code: 'rule.invalid', params: {} })
    expect(err('rule.invalid', 'field')).toEqual({ code: 'rule.invalid', params: {} })
    expect(err('rule.invalid', 'field', 'amount')).toEqual({
      code: 'rule.invalid',
      params: { field: 'amount' },
    })
  })
})
