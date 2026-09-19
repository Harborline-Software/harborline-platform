import type { FormViewField as CanonicalFormViewField } from '@harborline-software/contracts/forms'
import { expect, it } from 'vitest'

import { FormViewField } from '../index'
import { sharedCases } from './fixtures'

interface DomainCase {
  id: string
  input: { field: CanonicalFormViewField; hostOptions: string[] }
  expected: { options: string[]; permittedValues?: string[]; redacted: boolean }
}

const cases = sharedCases.filter(value => value.id.startsWith('form-view.domain-')) as DomainCase[]
expect(cases.map(value => value.id)).toEqual([
  'form-view.domain-permitted',
  'form-view.domain-empty',
  'form-view.domain-unreadable',
  'form-view.domain-sensitive',
  'form-view.domain-redacted-host-options',
])

// Host option precedence or retained redacted membership must break this binding contract.
it.each(cases)('$id', ({ input, expected }) => {
  const before = JSON.stringify(input.field)
  const result = FormViewField.normalize(input.field, {
    options: input.hostOptions.map(value => ({ value, label: value })),
    readOnly: false,
  })

  expect(result.options?.map(option => option.value)).toEqual(expected.options)
  expect(result.permittedValues).toEqual(expected.permittedValues)
  expect(result.controlHint).toEqual(input.field.controlHint)
  expect(JSON.stringify(input.field)).toBe(before)
  if (expected.redacted) {
    expect(result.value).toBeNull()
    expect(result.readOnly).toBe(true)
    expect(JSON.stringify(result)).not.toContain('secret')
  }
})
