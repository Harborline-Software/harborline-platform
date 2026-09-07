/**
 * WF-KEY grammar extension (ADR 0140) — the net-new `wf.` / `timer.` process-context
 * addressing prefixes lower CANONICALLY (no `field.` rewrite) and are NOT extracted as
 * record-field dependencies. This is the only net-new contract surface WF-KEY adds to
 * the SPINE-1 §1.2 grammar; it keeps ONE addressing grammar across forms + workflows.
 *
 * Additive-safety: a form rule that does NOT use a wf./timer. prefix lowers byte-for-byte
 * as before — proven by the unchanged `field.` / bare-name / `self` / `section.` cases.
 */
import { describe, it, expect } from 'vitest'

import { lower, extractRefs, type LowerContext } from '../grammar.js'

const schemaCtx: LowerContext = { scope: 'Schema', scopeTarget: '', sectionId: null }

function varPath(ast: unknown): string {
  const v = (ast as { var?: unknown }).var
  return Array.isArray(v) ? String(v[0]) : String(v)
}

describe('WF-KEY wf./timer. process-context grammar', () => {
  it('lowers wf.state / wf.actor / wf.iteration canonically (no field. rewrite)', () => {
    for (const path of ['wf.state', 'wf.actor', 'wf.iteration']) {
      const ast = lower(JSON.stringify({ var: path }), schemaCtx, 'g')
      expect(varPath(ast)).toBe(path)
    }
  })

  it('lowers timer.<id> canonically', () => {
    const ast = lower(JSON.stringify({ var: 'timer.sla-clock' }), schemaCtx, 'g')
    expect(varPath(ast)).toBe('timer.sla-clock')
  })

  it('still rewrites a bare/field. name to field. (additive — no regression)', () => {
    expect(varPath(lower(JSON.stringify({ var: 'amount' }), schemaCtx, 'g'))).toBe('field.amount')
    expect(varPath(lower(JSON.stringify({ var: 'field.amount' }), schemaCtx, 'g'))).toBe('field.amount')
  })

  it('a mixed workflow guard reads form data AND process state through one lowering', () => {
    // `field.amount > 5000 and wf.iteration < 3` — the design's worked example.
    const expr = JSON.stringify({
      and: [
        { '>': [{ var: 'field.amount' }, 5000] },
        { '<': [{ var: 'wf.iteration' }, 3] },
      ],
    })
    const ast = lower(expr, schemaCtx, 'guard-1') as Record<string, unknown>
    const and = ast.and as unknown[]
    const gt = (and[0] as Record<string, unknown>)['>'] as unknown[]
    const lt = (and[1] as Record<string, unknown>)['<'] as unknown[]
    expect(varPath(gt[0])).toBe('field.amount')
    expect(varPath(lt[0])).toBe('wf.iteration')
  })

  it('does NOT extract wf./timer. as field/row deps (they are external context, not cells)', () => {
    const expr = JSON.stringify({
      and: [
        { '>': [{ var: 'field.amount' }, 5000] },
        { '<': [{ var: 'wf.iteration' }, 3] },
        { '>': [{ var: 'timer.sla-clock' }, 0] },
      ],
    })
    const ast = lower(expr, schemaCtx, 'guard-1')
    const refs = extractRefs(ast)
    // Only the real form field participates in the dependency graph.
    expect(refs).toEqual([{ kind: 'field', name: 'amount' }])
  })
})
