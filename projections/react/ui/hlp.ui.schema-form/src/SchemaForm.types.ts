import type * as React from 'react'

import type { RuleEvaluationResult } from '@harborline-software/rule-engine'
import type {
  FormActionConfig,
  FormValues,
  FormView,
  FormViewField,
  ValidationResult,
} from '@harborline-platform/hlp.ui.form-view'

export interface SchemaFormStrings {
  submit: string
  submitting: string
  errorSummaryTitle: string
  redacted: string
  empty: string
  addItem: string
  removeItem: string
  itemLabel: string
  controlUnavailable: string
  submitBlocked: string
}

export interface RuleInstanceLike {
  fields: Record<string, unknown>
  tables: Record<string, unknown>
}

export interface RuleGraphLike {
  evaluateInstance(instance: RuleInstanceLike): RuleEvaluationResult
}

export interface ControlArgs {
  field: FormViewField
  chain: readonly string[]
  value: unknown
  strValue: string
  hasError: boolean
  required: boolean
  disabled: boolean
  strings: SchemaFormStrings
  onChange: (value: unknown) => void
}

export type ControlRenderer = (args: ControlArgs) => React.ReactElement

export type ControlRegistry = Record<string, ControlRenderer> & { text: ControlRenderer }

export interface SchemaFormProps {
  view: FormView
  ruleGraph?: RuleGraphLike | null
  initialValues?: FormValues
  values?: FormValues
  onSubmit: (values: FormValues) => Promise<ValidationResult | void> | ValidationResult | void
  onChange?: (values: FormValues) => void
  onValuesChange?: (values: FormValues) => void
  strings?: Partial<SchemaFormStrings>
  localeChain?: readonly string[]
  className?: string
  disabled?: boolean
  controls?: Record<string, ControlRenderer>
  onBlockAction?: (action: FormActionConfig) => void
}
