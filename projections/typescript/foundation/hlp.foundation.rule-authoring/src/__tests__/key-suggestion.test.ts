import { expect, it } from 'vitest'
import { suggestRuleKey } from '../key-suggestion.js'
it('suggests without allocation or a version suffix', () => {
  const taken = new Set(['invoice-route', 'invoice-route-2'])
  expect(suggestRuleKey('  Invoice Route!  ', taken)).toBe('invoice-route-3')
  expect(suggestRuleKey('!!!', taken)).toBe('rule')
  expect(taken.size).toBe(2)
})
