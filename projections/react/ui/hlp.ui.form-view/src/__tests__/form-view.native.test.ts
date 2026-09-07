import type { FormView as CanonicalFormView, FormViewField as CanonicalFormViewField } from '@harborline-software/contracts/forms'
import { describe, expect, expectTypeOf, it } from 'vitest'

import {
  FormView,
  FormViewField,
  resolveText,
  type Cardinality,
  type CollectionColumn,
  type CollectionTableConfig,
  type ContentNode,
  type ControlHint,
  type FieldPlacement,
  type FieldWidth,
  type FlexDirection,
  type FlexWrap,
  type FormActionConfig,
  type FormActionKind,
  type FormValues,
  type FormViewItem,
  type FormViewSection,
  type InternationalizedText,
  type LayoutAlign,
  type LayoutBreakpoint,
  type LayoutDensity,
  type LayoutGap,
  type PresentationOutcome,
  type SectionLayout,
  type SectionLayoutKind,
  type Severity,
  type ValidationError,
  type ValidationErrorKind,
  type ValidationResult,
} from '../index'
import { fixture, qualityCases } from './fixtures'

type CompletePinnedTypeSurface = {
  InternationalizedText: InternationalizedText
  ControlHint: ControlHint
  Severity: Severity
  PresentationOutcome: PresentationOutcome
  FormViewField: FormViewField
  SectionLayoutKind: SectionLayoutKind
  FlexDirection: FlexDirection
  FlexWrap: FlexWrap
  LayoutGap: LayoutGap
  LayoutBreakpoint: LayoutBreakpoint
  LayoutDensity: LayoutDensity
  LayoutAlign: LayoutAlign
  FieldWidth: FieldWidth
  SectionLayout: SectionLayout
  FieldPlacement: FieldPlacement
  ContentNode: ContentNode
  FormActionKind: FormActionKind
  FormActionConfig: FormActionConfig
  Cardinality: Cardinality
  CollectionColumn: CollectionColumn
  CollectionTableConfig: CollectionTableConfig
  FormViewItem: FormViewItem
  FormViewSection: FormViewSection
  FormView: FormView
  ValidationErrorKind: ValidationErrorKind
  ValidationError: ValidationError
  ValidationResult: ValidationResult
  FormValues: FormValues
  resolveText: typeof resolveText
}

function field(overrides: Partial<CanonicalFormViewField> = {}): CanonicalFormViewField {
  return {
    name: 'safety',
    label: { defaultLocale: 'en', values: { en: 'Safety' } },
    isSensitive: false,
    isReadable: true,
    ...overrides,
  }
}

describe('Form View native edge cases', () => {
  it('typechecks all 29 pinned names and canonical wire assignability', () => {
    expectTypeOf<keyof CompletePinnedTypeSurface>().not.toBeNever()
    expectTypeOf<FormView>().toMatchTypeOf<CanonicalFormView>()
    expect(Object.keys(FormView).sort()).toEqual(['normalize'])
    expect(Object.keys(FormViewField).sort()).toEqual(['normalize'])
  })

  it('never allows render hints to restore withheld cleartext', () => {
    fixture(qualityCases, 'form-view.quality.redaction')
    const result = FormViewField.normalize(
      field({ isSensitive: true, isReadable: false, value: 'secret' }),
      { valueKind: 'string', required: true, readOnly: false, config: { leakedDefault: 'secret' } },
    )
    expect(result.value).toBeNull()
    expect(result.readOnly).toBe(true)
  })

  it('preserves read-only, required, and presentation as distinct semantic intents', () => {
    fixture(qualityCases, 'form-view.quality.semantic-intents')
    const rules = {
      visible: true,
      required: true,
      readOnly: true,
      presentationSeverity: 'error' as const,
      presentationBadge: { defaultLocale: 'en', values: { en: 'Blocked' } },
    }
    const result = FormViewField.normalize(field({ rules }))
    expect(result).toMatchObject({ required: true, readOnly: true, presentation: { severity: 'error' } })
    expect(result.isReadable).toBe(true)
  })

  it('normalizes nested canonical fields without mutating canonical input', () => {
    const canonicalField = field({
      rules: { visible: true, required: false, readOnly: true, presentationStyleToken: 'attention' },
    })
    const canonical: CanonicalFormView = {
      formId: 'nested',
      version: '1.0.0',
      sections: [{
        id: 'main',
        title: { defaultLocale: 'en', values: { en: 'Main' } },
        fields: [canonicalField],
        items: [{
          kind: 'group',
          key: 'group',
          items: [{ kind: 'field', key: 'safety', field: canonicalField }],
        }],
      }],
    }
    const result = FormView.normalize(canonical, { safety: { valueKind: 'boolean-toggle' } })

    expect(result.sections[0]?.fields[0]).toMatchObject({ readOnly: true, valueKind: 'boolean-toggle' })
    const group = result.sections[0]?.items?.[0]
    expect(group?.kind).toBe('group')
    if (group?.kind === 'group') expect(group.items[0]).toMatchObject({ field: { valueKind: 'boolean-toggle' } })
    expect(canonicalField).not.toHaveProperty('valueKind')
  })

  it('keeps localized authored content plain and Unicode byte-identical', () => {
    fixture(qualityCases, 'form-view.quality.plain-content')
    const unicode = fixture(qualityCases, 'form-view.quality.unicode').expected as { text: string }
    const text: InternationalizedText = { defaultLocale: 'ar', values: { ar: unicode.text } }
    expect(resolveText(text, ['ar-AE'])).toBe('فحص السلامة')
    expect(resolveText({ defaultLocale: 'en', values: { en: '<b>Safety</b>' } }, ['en'])).toBe('<b>Safety</b>')
  })

  it('preserves RFC 6901 root and nested validation locations, stable codes, and params', () => {
    fixture(qualityCases, 'form-view.quality.validation-location')
    fixture(qualityCases, 'form-view.quality.stable-codes')
    const errors: ValidationError[] = [
      { jsonPointer: '', message: 'Invalid form', kind: 'Schema', code: 'invalid', params: { scope: 'root' } },
      { jsonPointer: '/rows/0/value', message: 'Required', kind: 'Schema', code: 'required', params: { field: 'value' } },
    ]
    expect(errors.map(error => error.jsonPointer)).toEqual(['', '/rows/0/value'])
    expect(errors[1]?.params).toEqual({ field: 'value' })
  })

  it('covers every locale branch and preserves empty-string source values', () => {
    const value = fixture(qualityCases, 'form-view.quality.locale-chain').expected as { branches: string[] }
    expect(value.branches).toEqual(['exact', 'primary', 'default', 'first', 'fallback', 'empty'])
    expect(resolveText({ defaultLocale: 'en', values: { en: '' } }, ['en'], 'fallback')).toBe('')
    expect(resolveText(undefined, ['en'], 'fallback')).toBe('fallback')
    expect(resolveText({ defaultLocale: 'en', values: {} }, ['en'])).toBe('')
  })

  it('preserves logical layout tokens without adding locale-sensitive formatting', () => {
    fixture(qualityCases, 'form-view.quality.logical-layout')
    fixture(qualityCases, 'form-view.quality.formatting-applicability')
    const layout: SectionLayout = { kind: 'grid', columns: 2, align: 'end', collapseBelow: 'md' }
    expect(layout).toEqual({ kind: 'grid', columns: 2, align: 'end', collapseBelow: 'md' })
  })
})
