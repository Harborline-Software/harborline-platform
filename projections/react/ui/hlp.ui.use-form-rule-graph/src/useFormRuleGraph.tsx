import * as React from 'react'

import type { RuleEvaluationResult } from '@harborline-software/rule-engine'
import type { FormValues, FormView } from '@harborline-platform/hlp.ui.form-view'
import {
  projectRuleOutcomes,
  SchemaForm,
  type RuleGraphLike,
  type SchemaFormProps,
} from '@harborline-platform/hlp.ui.schema-form'

export type { RuleGraphLike }
export { projectRuleOutcomes }

export interface UseFormRuleGraphResult {
  /** The reactively projected view to hand to a controlled SchemaForm. */
  view: FormView
  /** Effective values: rule-computed Resolved cells merged over the user's. */
  values: FormValues
  /** Apply the next candidate map (the SchemaForm controlled-mode callback). */
  onValuesChange: (next: FormValues) => void
  /** Set a single field for a host that knows the changed cell. */
  setValue: (name: string, value: unknown) => void
  /** The latest raw evaluation (validations, save gate, …); null without a graph or after a throw. */
  evaluation: RuleEvaluationResult | null
  /** Fail-closed save gate: pending, errored, or throwing evaluation blocks save. */
  saveBlocked: boolean
}

/**
 * Drive a reactive FormView from an injected rule graph. Holds the user's
 * candidate values, re-evaluates the graph statelessly on every change, and
 * returns the projected view plus effective values for a controlled SchemaForm.
 *
 * `graph` may be null (no rules) — exact pass-through of the base view and the
 * raw values with an open save gate.
 */
export function useFormRuleGraph(
  graph: RuleGraphLike | null,
  baseView: FormView,
  opts: { initialValues?: FormValues } = {},
): UseFormRuleGraphResult {
  const [userValues, setUserValues] = React.useState<FormValues>(() => ({ ...opts.initialValues }))

  // Full stateless re-evaluation per change: correct across frequent graph
  // swaps (a new definition means a new graph, and this memo re-seeds cleanly).
  const evaluated = React.useMemo<{ evaluation: RuleEvaluationResult | null; error: unknown }>(() => {
    if (!graph) return { evaluation: null, error: null }
    try {
      return { evaluation: graph.evaluateInstance({ fields: userValues, tables: {} }), error: null }
    } catch (error) {
      // form-rule-graph.evaluation-failed: the base view passes through and the save gate blocks.
      return { evaluation: null, error }
    }
  }, [graph, userValues])

  const projected = React.useMemo(
    () => projectRuleOutcomes(baseView, evaluated.evaluation),
    [baseView, evaluated.evaluation],
  )
  const values = React.useMemo<FormValues>(
    () => ({ ...userValues, ...projected.computed }),
    [userValues, projected.computed],
  )

  const onValuesChange = React.useCallback((next: FormValues) => setUserValues(next), [])
  const setValue = React.useCallback(
    (name: string, value: unknown) => setUserValues(prev => ({ ...prev, [name]: value })),
    [],
  )

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
    onValuesChange,
    setValue,
    evaluation: evaluated.evaluation,
    saveBlocked,
  }
}

/**
 * A thin SchemaForm wrapper that drives the reactive projection from an
 * injected rule graph. Hand it the unprojected base view plus the compiled
 * graph; it renders the controlled, rule-evaluated form. It composes the
 * frozen schema-form renderer and forks no rendering of its own.
 */
export type ReactiveSchemaFormProps =
  Omit<SchemaFormProps, 'view' | 'values' | 'onValuesChange' | 'ruleGraph'> & {
    /** The compiled rule graph (injected). Null means no rules. */
    graph: RuleGraphLike | null
    /** The unprojected base view; rules omit or flag as needed. */
    view: FormView
    /** Seed values for the candidate instance. */
    initialValues?: FormValues
  }

export function ReactiveSchemaForm({ graph, view: baseView, initialValues, ...rest }: ReactiveSchemaFormProps) {
  const { view, values, onValuesChange } = useFormRuleGraph(graph, baseView, { initialValues })
  return <SchemaForm view={view} values={values} onValuesChange={onValuesChange} {...rest} />
}
