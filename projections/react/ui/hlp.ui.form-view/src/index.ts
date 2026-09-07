import type {
  FormView as CanonicalFormView,
  FormViewField as CanonicalFormViewField,
  FormViewItem as CanonicalFormViewItem,
  FormViewSection as CanonicalFormViewSection,
  InternationalizedText,
  PresentationOutcome,
} from '@harborline-software/contracts/forms'

export type {
  Cardinality,
  CollectionColumn,
  CollectionTableConfig,
  ContentNode,
  ControlHint,
  FieldPlacement,
  FieldWidth,
  FlexDirection,
  FlexWrap,
  FormActionConfig,
  FormActionKind,
  InternationalizedText,
  LayoutAlign,
  LayoutBreakpoint,
  LayoutDensity,
  LayoutGap,
  PresentationOutcome,
  SectionLayout,
  SectionLayoutKind,
  Severity,
  ValidationError,
  ValidationErrorKind,
  ValidationResult,
} from '@harborline-software/contracts/forms'

/** Candidate document shape exposed by the pinned SchemaForm support surface. */
export type FormValues = Record<string, unknown>

interface FormViewFieldRenderHints {
  valueKind?: string
  required?: boolean
  options?: ReadonlyArray<{ value: string; label?: InternationalizedText | string }>
  readOnly?: boolean
  presentation?: PresentationOutcome
  config?: Record<string, unknown> | null
}

/**
 * Renderer binding over the canonical wire field. Canonical properties,
 * including `rules`, are inherited rather than re-declared; the added members
 * are UI-only hints from the pinned application-facing surface.
 */
export interface FormViewField extends CanonicalFormViewField, FormViewFieldRenderHints {}

type CanonicalFieldItem = Extract<CanonicalFormViewItem, { kind: 'field' }>
type CanonicalGroupItem = Extract<CanonicalFormViewItem, { kind: 'group' }>
type CanonicalCollectionItem = Extract<CanonicalFormViewItem, { kind: 'collection' }>
type CanonicalContentItem = Extract<CanonicalFormViewItem, { kind: 'content' }>
type CanonicalActionItem = Extract<CanonicalFormViewItem, { kind: 'action' }>

/** Recursive renderer binding derived from the canonical item union. */
export type FormViewItem =
  | (Omit<CanonicalFieldItem, 'field'> & { field: FormViewField })
  | (Omit<CanonicalGroupItem, 'items'> & { items: FormViewItem[] })
  | (Omit<CanonicalCollectionItem, 'items'> & { items: FormViewItem[] })
  | CanonicalContentItem
  | CanonicalActionItem

/** Section binding derived from the canonical section contract. */
export type FormViewSection = Omit<CanonicalFormViewSection, 'fields' | 'items'> & {
  fields: FormViewField[]
  items?: FormViewItem[]
}

/** Form binding derived from the canonical revision-4 wire contract. */
export type FormView = Omit<CanonicalFormView, 'sections'> & {
  sections: FormViewSection[]
}

type FieldHintsByName = Readonly<Record<string, FormViewFieldRenderHints | undefined>>

function presentationFromRules(
  field: CanonicalFormViewField,
  hints: FormViewFieldRenderHints,
): PresentationOutcome | undefined {
  const base = hints.presentation ?? field.presentation
  const rules = field.rules
  if (!rules) return base

  const hasRulePresentation = rules.presentationSeverity !== undefined
    || rules.presentationBadge !== undefined
    || rules.presentationStyleToken !== undefined
  if (!hasRulePresentation) return base

  return {
    ...(base ?? {}),
    ...(rules.presentationSeverity !== undefined ? { severity: rules.presentationSeverity } : {}),
    ...(rules.presentationBadge !== undefined ? { badge: rules.presentationBadge } : {}),
    ...(rules.presentationStyleToken !== undefined ? { styleToken: rules.presentationStyleToken } : {}),
  }
}

function normalizeFormViewField(
  field: CanonicalFormViewField,
  hints: FormViewFieldRenderHints = {},
): FormViewField {
  const redacted = field.isSensitive || !field.isReadable
  const normalized: FormViewField = {
    ...field,
    ...(hints.valueKind !== undefined ? { valueKind: hints.valueKind } : {}),
    ...(hints.options !== undefined ? { options: hints.options } : {}),
    ...(hints.config !== undefined ? { config: hints.config } : {}),
  }

  const required = field.rules?.required ?? hints.required
  if (required !== undefined) normalized.required = required

  const readOnly = redacted
    ? true
    : (field.rules?.readOnly ?? hints.readOnly ?? field.readOnly)
  if (readOnly !== undefined) normalized.readOnly = readOnly

  const presentation = presentationFromRules(field, hints)
  if (presentation !== undefined) normalized.presentation = presentation

  // UI hints can never restore a value withheld by the canonical engine.
  if (redacted) normalized.value = null

  return normalized
}

function normalizeFormViewItem(item: CanonicalFormViewItem, hints: FieldHintsByName): FormViewItem {
  switch (item.kind) {
    case 'field':
      return { ...item, field: normalizeFormViewField(item.field, hints[item.field.name]) }
    case 'group':
      return { ...item, items: item.items.map(child => normalizeFormViewItem(child, hints)) }
    case 'collection':
      return { ...item, items: item.items.map(child => normalizeFormViewItem(child, hints)) }
    case 'content':
    case 'action':
      return item
  }
}

function normalizeFormView(view: CanonicalFormView, hints: FieldHintsByName = {}): FormView {
  return {
    ...view,
    sections: view.sections.map(section => ({
      ...section,
      fields: section.fields.map(field => normalizeFormViewField(field, hints[field.name])),
      ...(section.items
        ? { items: section.items.map(item => normalizeFormViewItem(item, hints)) }
        : {}),
    })),
  }
}

/** Explicit canonical-rules-to-render-field normalization seam. */
export const FormViewField = Object.freeze({ normalize: normalizeFormViewField })

/** Explicit recursive canonical-view-to-render-view normalization seam. */
export const FormView = Object.freeze({ normalize: normalizeFormView })

/**
 * Resolve localized text with the exact pinned earlier source lookup order: each
 * requested full tag and primary language, declared default, first value,
 * explicit fallback, then the default empty fallback.
 */
export function resolveText(
  text: InternationalizedText | null | undefined,
  localeChain: readonly string[],
  fallback = '',
): string {
  if (!text) return fallback
  const { values, defaultLocale } = text
  for (const tag of localeChain) {
    if (tag in values) return values[tag]!
    const primary = tag.split('-')[0]
    if (primary && primary in values) return values[primary]!
  }
  if (defaultLocale && defaultLocale in values) return values[defaultLocale]!
  const first = Object.values(values)[0]
  return first ?? fallback
}
