import * as React from 'react'

import type { RuleEvaluationResult } from '@harborline-software/rule-engine'
import type {
  FormValues,
  FormView,
  FormViewField,
  FormViewItem,
  PresentationOutcome,
} from '@harborline-platform/hlp.ui.form-view'

import type { RuleGraphLike } from './SchemaForm.types'

const fieldKey = (name: string): string => `field:${name}`
const sectionKey = (id: string): string => `section:${id}`

function presentations(evaluation: RuleEvaluationResult): Map<string, PresentationOutcome> {
  const byTarget = new Map<string, PresentationOutcome>()
  for (const outcome of evaluation.byRule.values()) {
    if (outcome.outputType !== 'Presentation' || !outcome.presentation) continue
    const value = outcome.presentation
    if (value.badge || value.severity || value.styleToken) byTarget.set(outcome.target, value)
  }
  return byTarget
}

function projectField(
  field: FormViewField,
  evaluation: RuleEvaluationResult,
  presentationByTarget: ReadonlyMap<string, PresentationOutcome>,
): FormViewField | null {
  const visibility = evaluation.visibility.get(fieldKey(field.name))
  if (visibility && !visibility.visible) return null
  const next = { ...field }
  if (visibility?.required) next.required = true
  if (visibility?.readOnly) next.readOnly = true
  const presentation = presentationByTarget.get(fieldKey(field.name))
  if (presentation) next.presentation = presentation
  return next
}

function projectItems(
  items: readonly FormViewItem[],
  evaluation: RuleEvaluationResult,
  presentationByTarget: ReadonlyMap<string, PresentationOutcome>,
): FormViewItem[] {
  const projected: FormViewItem[] = []
  for (const item of items) {
    if (item.kind === 'field') {
      const field = projectField(item.field, evaluation, presentationByTarget)
      if (field) projected.push({ ...item, field })
      continue
    }
    if (item.kind === 'group' || item.kind === 'collection') {
      projected.push({ ...item, items: projectItems(item.items, evaluation, presentationByTarget) })
      continue
    }
    projected.push(item)
  }
  return projected
}

export function projectRuleOutcomes(
  baseView: FormView,
  evaluation: RuleEvaluationResult | null,
): { view: FormView; computed: FormValues } {
  if (!evaluation) return { view: baseView, computed: {} }

  const computed: FormValues = {}
  for (const [key, value] of evaluation.values) {
    if (value.state === 'Resolved' && key.startsWith('field:')) {
      computed[key.slice('field:'.length)] = value.value
    }
  }

  const presentationByTarget = presentations(evaluation)
  const sections = baseView.sections.flatMap(section => {
    const visibility = evaluation.visibility.get(sectionKey(section.id))
    if (visibility && !visibility.visible) return []
    return [{
      ...section,
      fields: section.fields.flatMap(field => {
        const projected = projectField(field, evaluation, presentationByTarget)
        return projected ? [projected] : []
      }),
      ...(section.items
        ? { items: projectItems(section.items, evaluation, presentationByTarget) }
        : {}),
    }]
  })
  return { view: { ...baseView, sections }, computed }
}

export interface UseFormRuleGraphOptions {
  initialValues?: FormValues
  values?: FormValues
  onChange?: (values: FormValues) => void
  onValuesChange?: (values: FormValues) => void
}

export interface UseFormRuleGraphResult {
  view: FormView
  values: FormValues
  setValues: (values: FormValues) => void
  evaluation: RuleEvaluationResult | null
  evaluationError: unknown
  saveBlocked: boolean
}

export function useFormRuleGraph(
  graph: RuleGraphLike | null,
  baseView: FormView,
  options: UseFormRuleGraphOptions = {},
): UseFormRuleGraphResult {
  const controlled = options.values !== undefined
  const [internalValues, setInternalValues] = React.useState<FormValues>(() => ({ ...options.initialValues }))
  const userValues = controlled ? options.values! : internalValues

  const evaluated = React.useMemo<{
    evaluation: RuleEvaluationResult | null
    error: unknown
  }>(() => {
    if (!graph) return { evaluation: null, error: null }
    try {
      return {
        evaluation: graph.evaluateInstance({ fields: userValues, tables: {} }),
        error: null,
      }
    } catch (error) {
      return { evaluation: null, error }
    }
  }, [graph, userValues])

  const projected = React.useMemo(
    () => projectRuleOutcomes(baseView, evaluated.evaluation),
    [baseView, evaluated.evaluation],
  )
  const values = React.useMemo(
    () => ({ ...userValues, ...projected.computed }),
    [userValues, projected.computed],
  )
  const setValues = React.useCallback((next: FormValues) => {
    options.onChange?.(next)
    options.onValuesChange?.(next)
    if (!controlled) setInternalValues(next)
  }, [controlled, options.onChange, options.onValuesChange])

  const outcomeBlocked = evaluated.evaluation
    ? [...evaluated.evaluation.values.values()].some(value => value.state === 'Pending' || value.state === 'Error')
    : false
  const saveBlocked = Boolean(graph) && (
    evaluated.error !== null
    || evaluated.evaluation === null
    || evaluated.evaluation.hasPending
    || evaluated.evaluation.isSaveBlocked
    || outcomeBlocked
  )

  return {
    view: projected.view,
    values,
    setValues,
    evaluation: evaluated.evaluation,
    evaluationError: evaluated.error,
    saveBlocked,
  }
}
