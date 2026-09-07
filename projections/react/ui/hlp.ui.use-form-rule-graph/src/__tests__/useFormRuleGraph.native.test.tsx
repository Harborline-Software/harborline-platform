import { fireEvent, render, screen } from '@testing-library/react'
import * as React from 'react'
import { describe, expect, it } from 'vitest'

import type { RuleEvaluationResult, VisibilityState } from '@harborline-software/rule-engine'
import type { FormView, InternationalizedText } from '@harborline-platform/hlp.ui.form-view'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { ReactiveSchemaForm, projectRuleOutcomes, useFormRuleGraph, type RuleGraphLike } from '../index'
import { fixture, qualityCases } from './fixtures'

const tt = (en: string): InternationalizedText => ({ defaultLocale: 'en', values: { en } })
const field = (name: string, label: string, over: Record<string, unknown> = {}) => ({
  name,
  label: tt(label),
  isSensitive: false,
  isReadable: true,
  ...over,
})

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

const baseView: FormView = {
  formId: 'f.v1',
  version: '1.0.0',
  title: tt('F'),
  sections: [{
    id: 's1',
    title: tt('S1'),
    fields: [
      field('a', 'Reveal'),
      field('b', 'Gated'),
      field('total', 'Total', { readOnly: true }),
    ],
  }],
}

const hideGraph: RuleGraphLike = {
  evaluateInstance: instance => evaluation({
    visibility: new Map([['field:b', vis(instance.fields.a === 'show')]]),
    values: new Map([['field:total', { state: 'Resolved', value: String((instance.fields.a as string | undefined)?.length ?? 0) }]]),
  }),
}

const renderInLocale = (node: React.ReactElement) =>
  render(<HarborlineLocaleProvider locale="en">{node}</HarborlineLocaleProvider>)

describe('useFormRuleGraph quality profile', () => {
  it('form-rule-graph.quality.hidden-not-rendered: a rule-hidden field contributes no markup', () => {
    const expected = fixture(qualityCases, 'form-rule-graph.quality.hidden-not-rendered').expected as {
      ruleHiddenFieldContributesNoMarkup: boolean
      assistiveTechnologySeesNoHiddenControl: boolean
    }
    expect(expected.ruleHiddenFieldContributesNoMarkup).toBe(true)
    expect(expected.assistiveTechnologySeesNoHiddenControl).toBe(true)
    const { container } = renderInLocale(
      <ReactiveSchemaForm graph={hideGraph} view={baseView} initialValues={{ a: '' }} onSubmit={() => {}} />,
    )
    // Omitted, not visually hidden: no input, no label, nothing for assistive technology.
    expect(container.querySelector('input[name="b"]')).toBeNull()
    expect(screen.queryByText('Gated')).toBeNull()
  })

  it('form-rule-graph.quality.read-only-association: a read-only field stays labelled with its value exposed', () => {
    const expected = fixture(qualityCases, 'form-rule-graph.quality.read-only-association').expected as {
      readOnlyFieldRemainsLabelled: boolean
      valueRemainsExposed: boolean
    }
    expect(expected.readOnlyFieldRemainsLabelled).toBe(true)
    expect(expected.valueRemainsExposed).toBe(true)
    const { container } = renderInLocale(
      <ReactiveSchemaForm graph={hideGraph} view={baseView} initialValues={{ a: 'show' }} onSubmit={() => {}} />,
    )
    const total = container.querySelector<HTMLInputElement>('input[name="total"]')!
    expect(total).not.toBeNull()
    expect(total.disabled).toBe(true)
    expect(total.value).toBe('4') // the computed value is shown, unlike a redacted row
    expect(screen.getByText('Total')).toBeTruthy()
  })
})

describe('useFormRuleGraph native behavior (pinned source parity)', () => {
  it('setValue applies a single-field update over the candidate map', () => {
    function Harness() {
      const { view, values, setValue } = useFormRuleGraph(hideGraph, baseView, { initialValues: { a: '' } })
      return (
        <div>
          <button onClick={() => setValue('a', 'show')}>reveal</button>
          <output data-testid="visible">{view.sections[0].fields.map(f => f.name).join(',')}</output>
          <output data-testid="total">{String(values.total)}</output>
        </div>
      )
    }
    renderInLocale(<Harness />)
    expect(screen.getByTestId('visible').textContent).toBe('a,total')
    expect(screen.getByTestId('total').textContent).toBe('0')
    fireEvent.click(screen.getByText('reveal'))
    expect(screen.getByTestId('visible').textContent).toBe('a,b,total')
    expect(screen.getByTestId('total').textContent).toBe('4')
  })

  it('onValuesChange replaces the candidate map wholesale', () => {
    function Harness() {
      const { values, onValuesChange } = useFormRuleGraph(null, baseView, { initialValues: { a: 'seed', b: 'kept' } })
      return (
        <div>
          <button onClick={() => onValuesChange({ a: 'next' })}>replace</button>
          <output data-testid="values">{JSON.stringify(values)}</output>
        </div>
      )
    }
    renderInLocale(<Harness />)
    fireEvent.click(screen.getByText('replace'))
    expect(JSON.parse(screen.getByTestId('values').textContent!)).toEqual({ a: 'next' })
  })

  it('ReactiveSchemaForm with a null graph is the LivePreview pass-through path over item trees', () => {
    const itemView: FormView = {
      formId: 'x',
      version: '1.0.0',
      title: tt('X'),
      sections: [{
        id: 'sec',
        title: tt('Sec'),
        fields: [],
        items: [
          { kind: 'collection', key: 'collA', title: tt('Collection A'), items: [{ kind: 'field', key: 'a1', field: field('a1', 'A1') }] },
          { kind: 'collection', key: 'collB', title: tt('Collection B'), items: [{ kind: 'field', key: 'b1', field: field('b1', 'B1') }] },
        ],
      }],
    }
    renderInLocale(<ReactiveSchemaForm graph={null} view={itemView} onSubmit={() => {}} />)
    expect(screen.getByTestId('collection-collA')).toBeTruthy()
    expect(screen.getByTestId('collection-collB')).toBeTruthy()
  })

  it('projectRuleOutcomes leaves content and action items untouched', () => {
    const view: FormView = {
      formId: 'x',
      version: '1.0.0',
      title: tt('X'),
      sections: [{
        id: 'sec',
        title: tt('Sec'),
        fields: [],
        items: [
          { kind: 'content', key: 'note', content: [{ kind: 'paragraph', text: tt('Read me') }] },
          { kind: 'field', key: 'a', field: field('a', 'A') },
        ],
      }],
    }
    const projected = projectRuleOutcomes(view, evaluation({ visibility: new Map([['field:a', vis(false)]]) }))
    const items = projected.view.sections[0].items!
    expect(items).toHaveLength(1)
    expect(items[0].kind).toBe('content')
  })

  it('an isSaveBlocked evaluation blocks save even when every value is Resolved', () => {
    const blocked: RuleGraphLike = {
      evaluateInstance: () => evaluation({ isSaveBlocked: true }),
    }
    function Harness() {
      const { saveBlocked } = useFormRuleGraph(blocked, baseView, {})
      return <output data-testid="gate">{String(saveBlocked)}</output>
    }
    renderInLocale(<Harness />)
    expect(screen.getByTestId('gate').textContent).toBe('true')
  })
})
