import type {
  FormView as CanonicalFormView,
  FormViewField as CanonicalFormViewField,
} from '@harborline-software/contracts/forms'
import { describe, expect, expectTypeOf, it } from 'vitest'

import {
  FormView,
  FormViewField,
  resolveText,
  type FormValues,
  type FormViewItem,
  type ValidationResult,
} from '../index'
import { fixture, sharedCases } from './fixtures'

const pinnedExportNames = [
  'InternationalizedText', 'ControlHint', 'Severity', 'PresentationOutcome', 'FormViewField',
  'SectionLayoutKind', 'FlexDirection', 'FlexWrap', 'LayoutGap', 'LayoutBreakpoint',
  'LayoutDensity', 'LayoutAlign', 'FieldWidth', 'SectionLayout', 'FieldPlacement', 'ContentNode',
  'FormActionKind', 'FormActionConfig', 'Cardinality', 'CollectionColumn', 'CollectionTableConfig',
  'FormViewItem', 'FormViewSection', 'FormView', 'ValidationErrorKind', 'ValidationError',
  'ValidationResult', 'FormValues', 'resolveText',
] as const

function canonicalField(overrides: Partial<CanonicalFormViewField> = {}): CanonicalFormViewField {
  return {
    name: 'amount',
    label: { defaultLocale: 'en', values: { en: 'Amount' } },
    isSensitive: false,
    isReadable: true,
    ...overrides,
  }
}

describe('Form View revision-1 shared fixtures', () => {
  it('preserves the complete 29-name pinned export surface', () => {
    const value = fixture(sharedCases, 'form-view.complete-export-surface')
    expect(pinnedExportNames).toHaveLength((value.input as { sourceExportCount: number }).sourceExportCount)
    expect(new Set(pinnedExportNames).size).toBe(29)
  })

  it('derives the binding from canonical revision-4 wire contracts', () => {
    const owner = fixture(sharedCases, 'form-view.canonical-owner').expected as { owner: string; minimumRevision: number }
    fixture(sharedCases, 'form-view.wire-identity')
    expect(owner).toEqual({ owner: 'hlp.contracts.forms', minimumRevision: 4 })
    expectTypeOf<FormView>().toMatchTypeOf<CanonicalFormView>()

    const wire: CanonicalFormView = { formId: 'inspection', version: '1.0.0', sections: [] }
    expect(JSON.parse(JSON.stringify(FormView.normalize(wire)))).toEqual(wire)
  })

  it('preserves a minimal view without inventing optional content', () => {
    const input = fixture(sharedCases, 'form-view.minimal').input as CanonicalFormView
    const result = FormView.normalize(input)
    expect(result).toEqual(input)
    expect('title' in result).toBe(false)
    expect('description' in result).toBe(false)
  })

  it('keeps readable values and fails closed for redacted values', () => {
    const readable = fixture(sharedCases, 'form-view.readable-value').input as Partial<CanonicalFormViewField>
    const redacted = fixture(sharedCases, 'form-view.redaction').input as Partial<CanonicalFormViewField>

    expect(FormViewField.normalize(canonicalField(readable)).value).toBe('A-104')
    expect(FormViewField.normalize(canonicalField({ ...redacted, value: 'must-not-leak' }))).toMatchObject({
      value: null,
      readOnly: true,
      isReadable: false,
    })
  })

  it('keeps read-only distinct from redaction and applies renderer-only hints', () => {
    const readOnly = fixture(sharedCases, 'form-view.readonly-distinct').input as Partial<CanonicalFormViewField>
    const hints = fixture(sharedCases, 'form-view.renderer-hints').input as {
      valueKind: string
      required: boolean
      options: ReadonlyArray<{ value: string; label: string }>
      config: Record<string, unknown>
    }
    const result = FormViewField.normalize(canonicalField(readOnly), hints)

    expect(result).toMatchObject({ value: 'approved', readOnly: true, isReadable: true, ...hints })
  })

  it('normalizes canonical rule evidence and preserves the original rules object', () => {
    const input = fixture(sharedCases, 'form-view.rules-normalization').input as {
      rules: NonNullable<CanonicalFormViewField['rules']>
    }
    const result = FormViewField.normalize(canonicalField(input))

    expect(result.readOnly).toBe(true)
    expect(result.required).toBe(true)
    expect(result.presentation).toEqual({ severity: 'warn', styleToken: 'attention' })
    expect(result.rules).toBe(input.rules)
  })

  it('preserves arbitrary-depth item trees, layout, and collection-table intents', () => {
    fixture(sharedCases, 'form-view.nested-items')
    const layout = fixture(sharedCases, 'form-view.layout').input
    const collection = fixture(sharedCases, 'form-view.collection-table').input as {
      cardinality: { min: number; max: number }
      table: { columns: Record<string, { width: '1/3'; align: 'end' }>; totals: string[] }
    }
    const field = canonicalField()
    const item: FormViewItem = {
      kind: 'group',
      key: 'root',
      items: [{
        kind: 'collection',
        key: 'rows',
        items: [{ kind: 'field', key: 'amount', field }],
        ...collection,
      }],
    }

    const view = FormView.normalize({
      formId: 'nested',
      version: '1.0.0',
      sections: [{
        id: 'main',
        title: { defaultLocale: 'en', values: { en: 'Main' } },
        fields: [field],
        layout: layout as CanonicalFormView['sections'][number]['layout'],
        items: [item],
      }],
    })

    expect(view.sections[0]?.layout).toEqual(layout)
    expect(view.sections[0]?.items).toEqual([item])
  })

  it('preserves authored content as text and host-delegated action config', () => {
    const input = fixture(sharedCases, 'form-view.content-action').input as {
      content: Array<{ kind: 'heading'; text: { defaultLocale: string; values: Record<string, string> }; level: number }>
      action: { kind: 'open-url'; label: { defaultLocale: string; values: Record<string, string> }; url: string }
    }
    expect(input.content[0]?.text.values.en).toBe('Safety <b>check</b>')
    expect(input.action).toMatchObject({ kind: 'open-url', url: 'https://example.invalid/guide' })
  })

  it('round-trips nested candidate JSON and decimal strings losslessly', () => {
    const input = fixture(sharedCases, 'form-view.candidate-roundtrip').input as FormValues
    const result = JSON.parse(JSON.stringify(input)) as FormValues
    expect(result).toEqual(input)
    expect(result.decimal).toBe('12.3400')
  })

  it('preserves validation success and structured validation failures', () => {
    const success = fixture(sharedCases, 'form-view.validation-success').input as ValidationResult
    const failure = fixture(sharedCases, 'form-view.validation-error').input as ValidationResult
    expect(success).toEqual({ isValid: true, errors: [] })
    expect(failure.errors[0]).toEqual({
      jsonPointer: '/sections/0/amount',
      message: 'Must be at least 1',
      kind: 'Schema',
      code: 'minimum',
      params: { min: '1' },
    })
  })

  it.each([
    'form-view.locale-exact',
    'form-view.locale-primary',
    'form-view.locale-default',
    'form-view.locale-first',
    'form-view.locale-fallback',
    'form-view.locale-empty',
  ])('implements exact pinned localization branch: %s', id => {
    const value = fixture(sharedCases, id)
    const input = value.input as {
      text: Parameters<typeof resolveText>[0]
      localeChain: string[]
      fallback?: string
    }
    expect(resolveText(input.text, input.localeChain, input.fallback)).toBe(value.expected)
  })
})
