import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import type { RuleEvaluationResult } from '@harborline-software/rule-engine'
import type {
  FormView,
  FormViewField,
  FormViewSection,
  InternationalizedText,
  PresentationOutcome,
} from '@harborline-platform/hlp.ui.form-view'

export interface FixtureCase {
  id: string
  expected: Record<string, unknown>
}

interface QualityDocument {
  accessibilityCases?: FixtureCase[]
  internationalizationCases?: FixtureCase[]
  themingCases?: FixtureCase[]
  performanceCases?: FixtureCase[]
}

const path = resolve(process.cwd(), '../../../../conformance/hlp.ui.schema-form/quality-fixtures.yaml')
const quality = JSON.parse(readFileSync(path, 'utf8')) as QualityDocument

export const qualityCases = [
  ...(quality.accessibilityCases ?? []),
  ...(quality.internationalizationCases ?? []),
  ...(quality.themingCases ?? []),
]
export const performanceCases = quality.performanceCases ?? []

export function fixture(cases: readonly FixtureCase[], id: string): FixtureCase {
  const value = cases.find(candidate => candidate.id === id)
  if (!value) throw new Error(`missing-neutral-fixture: ${id}`)
  return value
}

export function text(value: string, locale = 'en'): InternationalizedText {
  return { defaultLocale: locale, values: { [locale]: value } }
}

export function field(name: string, label = name, overrides: Partial<FormViewField> = {}): FormViewField {
  return {
    name,
    label: text(label),
    isSensitive: false,
    isReadable: true,
    controlHint: 'text',
    ...overrides,
  }
}

export function section(id: string, fields: FormViewField[], overrides: Partial<FormViewSection> = {}): FormViewSection {
  return { id, title: text(id), fields, ...overrides }
}

export function form(sections: FormViewSection[], title = 'Fixture form'): FormView {
  return { formId: 'fixture', version: '1.0.0', title: text(title), sections }
}

interface EvaluationOptions {
  visibility?: Array<readonly [string, { visible: boolean; required: boolean; readOnly: boolean }]>
  values?: Array<readonly [string, { state: 'Resolved' | 'Pending' | 'Error'; value?: unknown }]>
  presentations?: Array<readonly [string, PresentationOutcome]>
  hasPending?: boolean
  isSaveBlocked?: boolean
}

export function evaluation(options: EvaluationOptions = {}): RuleEvaluationResult {
  const byRule = new Map(options.presentations?.map(([target, presentation], index) => [
    `presentation-${index}`,
    { ruleId: `presentation-${index}`, target, outputType: 'Presentation', presentation },
  ]))
  return {
    byRule,
    values: new Map(options.values),
    visibility: new Map(options.visibility),
    validations: [],
    options: new Map(),
    hasPending: options.hasPending ?? false,
    isSaveBlocked: options.isSaveBlocked ?? false,
  } as RuleEvaluationResult
}
