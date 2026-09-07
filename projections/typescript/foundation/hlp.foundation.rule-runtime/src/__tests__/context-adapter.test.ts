/**
 * The ADR 0146 D3 context-adapter seam (TS tier). Pins the public contract
 * (`ContextAdapter` / `ValueResolver` / `RefValue` / `RuleEvalScope` / `ROOT_SCOPE`) and that
 * an arbitrary (Wave-2-style) consumer can implement it against only the exported types.
 * Behaviour-neutrality of the wider migration is covered by the conformance / reactive suites,
 * which now obtain their resolvers through this seam.
 */
import { describe, it, expect } from 'vitest'

import { ROOT_SCOPE } from '../index.js'
import type { ContextAdapter, RuleEvalScope, RefValue, ValueResolver } from '../index.js'
import type { Json } from '../model.js'

/**
 * A minimal external pillar adapter written against ONLY the public seam — the shape Wave 2's
 * document/event/dataset adapters take (own the seam, scatter the implementations).
 */
class EchoPillarAdapter implements ContextAdapter {
  constructor(private readonly data: Record<string, Json>) {}

  createResolver(_scope: RuleEvalScope): ValueResolver {
    const data = this.data
    return {
      resolveVar(path: string): RefValue {
        const name = path.startsWith('field.') ? path.slice('field.'.length) : path
        return name in data
          ? { state: 'Resolved', value: data[name] }
          : { state: 'Error', error: { code: 'test.absent', params: { path } } }
      },
      resolveAgg(fn: string, section: string, col: string): RefValue {
        return { state: 'Error', error: { code: 'test.no_agg', params: { agg: `${section}/${fn}/${col}` } } }
      },
    }
  }
}

describe('ADR 0146 D3 context-adapter seam', () => {
  it('ROOT_SCOPE is the top-level (rowless) scope', () => {
    expect(ROOT_SCOPE).toEqual({ rowSection: null, rowId: null })
  })

  it('an arbitrary consumer can implement the public seam', () => {
    const adapter: ContextAdapter = new EchoPillarAdapter({ name: 'acme' })
    const resolver = adapter.createResolver(ROOT_SCOPE)

    expect(resolver.resolveVar('field.name')).toEqual({ state: 'Resolved', value: 'acme' })
    expect(resolver.resolveVar('name')).toEqual({ state: 'Resolved', value: 'acme' }) // bare name = field
    expect(resolver.resolveVar('field.absent').state).toBe('Error')
    expect(resolver.resolveAgg('sum', 'items', 'amount').state).toBe('Error')
  })
})
