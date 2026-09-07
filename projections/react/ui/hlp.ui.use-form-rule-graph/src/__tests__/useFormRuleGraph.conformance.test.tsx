import { fireEvent, render, renderHook, act } from '@testing-library/react'
import * as React from 'react'
import { describe, expect, it } from 'vitest'

import type { RuleEvaluationResult, VisibilityState } from '@harborline-software/rule-engine'
import type { FormValues, FormView, InternationalizedText } from '@harborline-platform/hlp.ui.form-view'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import * as moduleSurface from '../index'
import {
  projectRuleOutcomes,
  ReactiveSchemaForm,
  useFormRuleGraph,
  type RuleGraphLike,
} from '../index'
import { fixture, sharedCases } from './fixtures'

const tt = (en: string): InternationalizedText => ({ defaultLocale: 'en', values: { en } })

const field = (name: string, over: Record<string, unknown> = {}) => ({
  name,
  label: tt(name.toUpperCase()),
  isSensitive: false,
  isReadable: true,
  ...over,
})

function viewWith(fields: ReturnType<typeof field>[], secondSectionFields?: ReturnType<typeof field>[]): FormView {
  const sections = [{ id: 's1', title: tt('S1'), fields }]
  if (secondSectionFields) sections.push({ id: 's2', title: tt('S2'), fields: secondSectionFields })
  return { formId: 'f.v1', version: '1.0.0', title: tt('F'), sections }
}

const vis = (visible: boolean, required = false, readOnly = false): VisibilityState => ({ visible, required, readOnly })

const evaluation = (over: Partial<RuleEvaluationResult>): RuleEvaluationResult => ({
  byRule: new Map(),
  values: new Map(),
  visibility: new Map(),
  validations: [],
  options: new Map(),
  hasPending: false,
  isSaveBlocked: false,
  ...over,
})

/** Fixture-driven fake engine: `b` is visible only when `a === revealValue`; `total` computes `a.length`. */
function revealGraph(revealValue: string): RuleGraphLike {
  return {
    evaluateInstance: instance => {
      const a = instance.fields.a as string | undefined
      return evaluation({
        visibility: new Map([['field:b', vis(a === revealValue)]]),
        values: new Map([['field:total', { state: 'Resolved', value: String(a?.length ?? 0) }]]),
      })
    },
  }
}

const renderInLocale = (node: React.ReactElement) =>
  render(<HarborlineLocaleProvider locale="en">{node}</HarborlineLocaleProvider>)

describe('hlp.ui.use-form-rule-graph revision-1 shared fixtures', () => {
  it('form-rule-graph.complete-surface: consumes the complete frozen surface', () => {
    const expected = fixture(sharedCases, 'form-rule-graph.complete-surface').expected as {
      exports: string[]
      count: number
      valueExports: number
    }
    expect(expected.count).toBe(6)
    const valueExports = Object.keys(moduleSurface)
    expect(valueExports.sort()).toEqual(['ReactiveSchemaForm', 'projectRuleOutcomes', 'useFormRuleGraph'])
    expect(valueExports).toHaveLength(expected.valueExports)
    for (const name of ['useFormRuleGraph', 'projectRuleOutcomes', 'ReactiveSchemaForm']) {
      expect(expected.exports).toContain(name)
    }
  })

  it('form-rule-graph.null-graph-pass-through: base view and raw values return unchanged', () => {
    const input = fixture(sharedCases, 'form-rule-graph.null-graph-pass-through').input as {
      fields: string[]
      values: FormValues
    }
    const expected = fixture(sharedCases, 'form-rule-graph.null-graph-pass-through').expected as {
      fieldOrder: string[]
      saveBlocked: boolean
    }
    const base = viewWith(input.fields.map(name => field(name)))
    const { result } = renderHook(() => useFormRuleGraph(null, base, { initialValues: input.values }))
    expect(result.current.view.sections[0].fields.map(f => f.name)).toEqual(expected.fieldOrder)
    expect(result.current.values).toEqual(input.values)
    expect(result.current.saveBlocked).toBe(expected.saveBlocked)
    expect(result.current.evaluation).toBeNull()
  })

  it('form-rule-graph.field-visibility-omission: a hidden field is omitted, order preserved', () => {
    const input = fixture(sharedCases, 'form-rule-graph.field-visibility-omission').input as {
      fields: string[]
      hiddenField: string
    }
    const expected = fixture(sharedCases, 'form-rule-graph.field-visibility-omission').expected as {
      visibleFields: string[]
    }
    const { view } = projectRuleOutcomes(
      viewWith(input.fields.map(name => field(name))),
      evaluation({ visibility: new Map([[`field:${input.hiddenField}`, vis(false)]]) }),
    )
    expect(view.sections[0].fields.map(f => f.name)).toEqual(expected.visibleFields)
  })

  it('form-rule-graph.section-visibility-omission: a hidden section is omitted whole', () => {
    const input = fixture(sharedCases, 'form-rule-graph.section-visibility-omission').input as {
      hiddenSection: string
    }
    const expected = fixture(sharedCases, 'form-rule-graph.section-visibility-omission').expected as {
      visibleSections: string[]
    }
    const { view } = projectRuleOutcomes(
      viewWith([field('a')], [field('c')]),
      evaluation({ visibility: new Map([[`section:${input.hiddenSection}`, vis(false)]]) }),
    )
    expect(view.sections.map(s => s.id)).toEqual(expected.visibleSections)
  })

  it('form-rule-graph.nested-item-projection: item trees project recursively', () => {
    const input = fixture(sharedCases, 'form-rule-graph.nested-item-projection').input as {
      hiddenNestedField: string
    }
    const expected = fixture(sharedCases, 'form-rule-graph.nested-item-projection').expected as {
      groupRetained: boolean
      collectionRetained: boolean
      visibleNestedFields: string[]
    }
    const base: FormView = {
      formId: 'f.v1',
      version: '1.0.0',
      title: tt('F'),
      sections: [{
        id: 's1',
        title: tt('S1'),
        fields: [],
        items: [{
          kind: 'group',
          key: 'sub',
          title: tt('Sub'),
          items: [{
            kind: 'collection',
            key: 'collA',
            title: tt('Collection A'),
            items: [
              { kind: 'field', key: 'a1', field: field('a1') },
              { kind: 'field', key: 'b1', field: field('b1') },
            ],
          }],
        }],
      }],
    }
    const { view } = projectRuleOutcomes(
      base,
      evaluation({ visibility: new Map([[`field:${input.hiddenNestedField}`, vis(false)]]) }),
    )
    const items = view.sections[0].items!
    const group = items[0]
    expect(group.kind === 'group').toBe(expected.groupRetained)
    if (group.kind !== 'group') throw new Error('group dropped')
    const collection = group.items[0]
    expect(collection.kind === 'collection').toBe(expected.collectionRetained)
    if (collection.kind !== 'collection') throw new Error('collection dropped')
    const nestedNames = collection.items.flatMap(item => (item.kind === 'field' ? [item.field.name] : []))
    expect(nestedNames).toEqual(expected.visibleNestedFields)
  })

  it('form-rule-graph.required-read-only-flags: flags set without clearing base flags', () => {
    const input = fixture(sharedCases, 'form-rule-graph.required-read-only-flags').input as {
      targetField: string
      required: boolean
      readOnly: boolean
      baseReadOnlyField: string
    }
    const { view } = projectRuleOutcomes(
      viewWith([field(input.targetField), field(input.baseReadOnlyField, { readOnly: true })]),
      evaluation({ visibility: new Map([[`field:${input.targetField}`, vis(true, input.required, input.readOnly)]]) }),
    )
    const [target, baseReadOnly] = view.sections[0].fields
    expect(target.required).toBe(true)
    expect(target.readOnly).toBe(true)
    expect(baseReadOnly.readOnly).toBe(true) // base flag preserved with no outcome
  })

  it('form-rule-graph.computed-value-injection: Resolved values merge over user values', () => {
    const input = fixture(sharedCases, 'form-rule-graph.computed-value-injection').input as {
      targetField: string
      value: string
      userValue: string
    }
    const expected = fixture(sharedCases, 'form-rule-graph.computed-value-injection').expected as {
      effectiveValue: string
    }
    const graph: RuleGraphLike = {
      evaluateInstance: () => evaluation({
        values: new Map([[`field:${input.targetField}`, { state: 'Resolved', value: input.value }]]),
      }),
    }
    const base = viewWith([field(input.targetField)])
    const { result } = renderHook(() =>
      useFormRuleGraph(graph, base, { initialValues: { [input.targetField]: input.userValue } }))
    expect(result.current.values[input.targetField]).toBe(expected.effectiveValue)
  })

  it('form-rule-graph.unresolved-value-not-injected: Pending and Error never inject and block save', () => {
    const input = fixture(sharedCases, 'form-rule-graph.unresolved-value-not-injected').input as {
      targetField: string
      states: ('Pending' | 'Error')[]
      userValue: string
    }
    const expected = fixture(sharedCases, 'form-rule-graph.unresolved-value-not-injected').expected as {
      effectiveValue: string
      saveBlocked: boolean
    }
    for (const state of input.states) {
      const graph: RuleGraphLike = {
        evaluateInstance: () => evaluation({
          values: new Map([[`field:${input.targetField}`, { state }]]),
        }),
      }
      const base = viewWith([field(input.targetField)])
      const { result } = renderHook(() =>
        useFormRuleGraph(graph, base, { initialValues: { [input.targetField]: input.userValue } }))
      expect(result.current.values[input.targetField]).toBe(expected.effectiveValue)
      expect(result.current.saveBlocked).toBe(expected.saveBlocked)
    }
  })

  it('form-rule-graph.presentation-attachment: outcomes attach to their target only; empty outcomes ignored', () => {
    const input = fixture(sharedCases, 'form-rule-graph.presentation-attachment').input as {
      targetField: string
      severity: 'error'
      badge: string
      untouchedField: string
    }
    const expected = fixture(sharedCases, 'form-rule-graph.presentation-attachment').expected as {
      attachedSeverity: string
    }
    const { view } = projectRuleOutcomes(
      viewWith([field(input.targetField), field(input.untouchedField)]),
      evaluation({
        byRule: new Map([
          ['p.target', {
            ruleId: 'p.target',
            target: `field:${input.targetField}`,
            outputType: 'Presentation',
            presentation: { severity: input.severity, badge: tt(input.badge) },
          }],
          ['p.empty', {
            ruleId: 'p.empty',
            target: `field:${input.untouchedField}`,
            outputType: 'Presentation',
            presentation: {},
          }],
        ]),
      }),
    )
    const [target, untouched] = view.sections[0].fields
    expect(target.presentation?.severity).toBe(expected.attachedSeverity)
    expect(untouched.presentation).toBeUndefined()
  })

  it('form-rule-graph.reactive-reevaluation: each change re-evaluates and re-projects', () => {
    const input = fixture(sharedCases, 'form-rule-graph.reactive-reevaluation').input as {
      gatedField: string
      revealField: string
      revealValue: string
      computedField: string
    }
    const base = viewWith([field(input.revealField), field(input.gatedField), field(input.computedField, { readOnly: true })])
    const { result } = renderHook(() =>
      useFormRuleGraph(revealGraph(input.revealValue), base, { initialValues: { [input.revealField]: '' } }))
    expect(result.current.view.sections[0].fields.map(f => f.name)).not.toContain(input.gatedField)
    expect(result.current.values[input.computedField]).toBe('0')
    act(() => result.current.setValue(input.revealField, input.revealValue))
    expect(result.current.view.sections[0].fields.map(f => f.name)).toContain(input.gatedField)
    expect(result.current.values[input.computedField]).toBe(String(input.revealValue.length))
  })

  it('form-rule-graph.graph-replacement: a new graph re-seeds with no stale outcome', () => {
    const input = fixture(sharedCases, 'form-rule-graph.graph-replacement').input as {
      firstGraphHides: string
      secondGraphHides: string
    }
    const hider = (name: string): RuleGraphLike => ({
      evaluateInstance: () => evaluation({ visibility: new Map([[`field:${name}`, vis(false)]]) }),
    })
    const base = viewWith([field('a'), field('b')])
    const { result, rerender } = renderHook(
      ({ graph }: { graph: RuleGraphLike }) => useFormRuleGraph(graph, base, {}),
      { initialProps: { graph: hider(input.firstGraphHides) } },
    )
    expect(result.current.view.sections[0].fields.map(f => f.name)).not.toContain(input.firstGraphHides)
    rerender({ graph: hider(input.secondGraphHides) })
    const names = result.current.view.sections[0].fields.map(f => f.name)
    expect(names).toContain(input.firstGraphHides) // stale omission not retained
    expect(names).not.toContain(input.secondGraphHides)
  })

  it('form-rule-graph.save-gate-fail-closed: pending, error, and throw all block; null graph stays open', () => {
    const expected = fixture(sharedCases, 'form-rule-graph.save-gate-fail-closed').expected as {
      saveBlockedInEveryMode: boolean
      nullGraphBlocked: boolean
      throwPassesBaseViewThrough: boolean
    }
    const base = viewWith([field('a')])
    const modes: Record<string, RuleGraphLike> = {
      pending: { evaluateInstance: () => evaluation({ hasPending: true }) },
      error: { evaluateInstance: () => evaluation({ values: new Map([['field:a', { state: 'Error' }]]) }) },
      throw: { evaluateInstance: () => { throw new Error('form-rule-graph.evaluation-failed') } },
    }
    for (const graph of Object.values(modes)) {
      const { result } = renderHook(() => useFormRuleGraph(graph, base, {}))
      expect(result.current.saveBlocked).toBe(expected.saveBlockedInEveryMode)
    }
    const thrown = renderHook(() => useFormRuleGraph(modes.throw, base, {}))
    expect(thrown.result.current.view.sections[0].fields.map(f => f.name)).toEqual(['a'])
    expect(expected.throwPassesBaseViewThrough).toBe(true)
    const open = renderHook(() => useFormRuleGraph(null, base, {}))
    expect(open.result.current.saveBlocked).toBe(expected.nullGraphBlocked)
  })

  it('form-rule-graph.reactive-wrapper: ReactiveSchemaForm renders the projected controlled form', () => {
    const input = fixture(sharedCases, 'form-rule-graph.reactive-wrapper').input as {
      gatedField: string
      revealValue: string
      computedField: string
    }
    const base = viewWith([field('a'), field(input.gatedField), field(input.computedField, { readOnly: true })])
    const { container } = renderInLocale(
      <ReactiveSchemaForm
        graph={revealGraph(input.revealValue)}
        view={base}
        initialValues={{ a: '' }}
        onSubmit={() => {}}
      />,
    )
    expect(container.querySelector(`input[name="${input.gatedField}"]`)).toBeNull()
    const revealInput = container.querySelector<HTMLInputElement>('input[name="a"]')!
    fireEvent.change(revealInput, { target: { value: input.revealValue } })
    expect(container.querySelector(`input[name="${input.gatedField}"]`)).not.toBeNull()
    const computed = container.querySelector<HTMLInputElement>(`input[name="${input.computedField}"]`)!
    expect(computed.value).toBe(String(input.revealValue.length))
    expect(computed.disabled).toBe(true) // base read-only flag survives projection
  })

  it('form-rule-graph.projection-equivalence: the shared sequence produces the deterministic canonical outcomes', () => {
    const input = fixture(sharedCases, 'form-rule-graph.projection-equivalence').input as {
      sequence: Record<string, string>[]
    }
    const expected = fixture(sharedCases, 'form-rule-graph.projection-equivalence').expected as {
      typescriptEqualsDotnet: boolean
    }
    expect(expected.typescriptEqualsDotnet).toBe(true)
    const base = viewWith([field('a'), field('b'), field('total', { readOnly: true })])
    const graph = revealGraph('show')
    const outcomes = input.sequence.map(fields => {
      const { view, computed } = projectRuleOutcomes(base, graph.evaluateInstance({ fields, tables: {} }))
      return {
        visible: view.sections[0].fields.map(f => f.name),
        total: computed.total,
      }
    })
    // The .NET projector answers the same fixture with the same canonical outcomes.
    expect(outcomes).toEqual([
      { visible: ['a', 'total'], total: '0' },
      { visible: ['a', 'b', 'total'], total: '4' },
      { visible: ['a', 'total'], total: '3' },
    ])
  })
})
