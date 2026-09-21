import * as React from 'react'

import { CheckBox } from '@harborline-platform/hlp.ui.check-box'
import { DateField } from '@harborline-platform/hlp.ui.date-field'
import { DateTimeField } from '@harborline-platform/hlp.ui.date-time-field'
import { useFormFieldContext } from '@harborline-platform/hlp.ui.form-field-context'
import { Input } from '@harborline-platform/hlp.ui.input'
import { NumberField } from '@harborline-platform/hlp.ui.number-field'
import { NumericTextBox } from '@harborline-platform/hlp.ui.numeric-text-box'
import { RadioGroup } from '@harborline-platform/hlp.ui.radio-group'
import { SelectField, type SelectOption } from '@harborline-platform/hlp.ui.select-field'
import { Switch } from '@harborline-platform/hlp.ui.switch'
import { TextArea } from '@harborline-platform/hlp.ui.text-area'
import { TextBox } from '@harborline-platform/hlp.ui.text-box'
import { resolveText } from '@harborline-platform/hlp.ui.form-view'

import type { ControlArgs, ControlRegistry } from './SchemaForm.types'

function fieldConfig(args: ControlArgs): Record<string, unknown> {
  return args.field.config ?? {}
}

function selectOptions(args: ControlArgs): SelectOption[] {
  if (args.field.permittedValues !== undefined) {
    return args.field.permittedValues.map(value => ({ value, label: value }))
  }
  return (args.field.options ?? []).map(option => ({
    value: option.value,
    label: typeof option.label === 'string'
      ? option.label
      : resolveText(option.label, args.chain, option.value),
  }))
}

function DomainPickerControl({ args }: { args: ControlArgs }) {
  const options = React.useMemo(() => selectOptions(args), [args.chain, args.field])
  if (args.field.permittedValues?.length === 0) return <></>
  return <SelectField searchable name={args.field.name} value={args.strValue}
    options={options} disabled={args.disabled} required={args.required} error={args.hasError}
    onValueChange={value => {
      if (!args.disabled && args.field.permittedValues?.includes(value)) args.onChange(value)
    }} />
}

const textControl = ({ field, strValue, hasError, disabled, onChange }: ControlArgs) => (
  <TextBox
    disabled={disabled}
    error={hasError}
    id={field.name}
    name={field.name}
    onChange={onChange}
    value={strValue}
  />
)

const numberControl = ({ field, strValue, hasError, disabled, onChange }: ControlArgs) => (
  <NumberField
    disabled={disabled}
    error={hasError}
    name={field.name}
    onChange={value => onChange(value === '' ? null : Number(value))}
    value={strValue}
  />
)

function CheckControl({ args }: { args: ControlArgs }) {
  const context = useFormFieldContext()
  return (
    <CheckBox
      aria-labelledby={context.labelId}
      checked={args.value === true}
      describedBy={context.describedBy}
      disabled={args.disabled}
      error={args.hasError}
      id={args.field.name}
      name={args.field.name}
      onChange={args.onChange}
      required={args.required}
    />
  )
}

function NativeInputControl({ args, type }: { args: ControlArgs; type: React.HTMLInputTypeAttribute }) {
  const context = useFormFieldContext()
  return (
    <Input
      aria-describedby={context.describedBy}
      aria-invalid={args.hasError || undefined}
      aria-labelledby={context.labelId}
      aria-required={args.required || undefined}
      disabled={args.disabled}
      id={args.field.name}
      name={args.field.name}
      onChange={event => args.onChange(event.currentTarget.value)}
      required={args.required}
      type={type}
      value={args.strValue}
    />
  )
}

function MultiSelectControl({ args }: { args: ControlArgs }) {
  const options = React.useMemo(() => selectOptions(args), [args.chain, args.field])
  const selected = React.useMemo(() => Array.isArray(args.value) ? args.value.map(String) : [], [args.value])
  return <SelectField multiple name={args.field.name} value={selected} options={options}
    disabled={args.disabled} required={args.required} error={args.hasError} onValueChange={args.onChange} />
}

function NumericControl({ args, kind }: { args: ControlArgs; kind: 'currency' | 'percentage' }) {
  const context = useFormFieldContext()
  const config = fieldConfig(args)
  const numericValue = args.value === '' || args.value == null ? null : Number(args.value)
  const value = numericValue !== null && Number.isFinite(numericValue) ? numericValue : null
  const decimals = typeof config.decimals === 'number' ? config.decimals : 2
  const currency = typeof config.currencyCode === 'string' ? config.currencyCode : 'USD'
  const min = typeof config.min === 'number' ? config.min : kind === 'percentage' ? 0 : undefined
  const max = typeof config.max === 'number' ? config.max : kind === 'percentage' ? 100 : undefined
  return (
    <NumericTextBox
      aria-describedby={context.describedBy}
      aria-labelledby={context.labelId}
      currency={currency}
      decimals={decimals}
      disabled={args.disabled}
      error={args.hasError}
      format={kind === 'currency' ? `c${decimals}` : undefined}
      id={args.field.name}
      locale={args.chain[0]}
      max={max}
      min={min}
      name={args.field.name}
      onChange={args.onChange}
      required={args.required}
      value={value}
    />
  )
}

function ReadonlyControl({ args }: { args: ControlArgs }) {
  const context = useFormFieldContext()
  return (
    <output
      aria-describedby={context.describedBy}
      aria-labelledby={context.labelId}
      className="hl-schema-form__readonly"
      id={args.field.name}
    >
      {args.strValue}
    </output>
  )
}

export function controlAcceptsValue(hint: string, value: unknown): boolean {
  if (value === null || value === undefined) return true
  if (hint === 'hidden') return true
  if (hint === 'checkbox' || hint === 'boolean' || hint === 'boolean-toggle') return typeof value === 'boolean'
  if (hint === 'multiselect') {
    return Array.isArray(value) && value.every(item => ['string', 'number', 'boolean'].includes(typeof item))
  }
  return typeof value === 'string' || typeof value === 'number'
}

// Select presentation is adapter policy; the runtime supplies the editor verdict.
const permittedChoiceControl = (args: ControlArgs) => (
  args.field.permittedValues?.length === 0 ? <></> : <SelectField
    disabled={args.disabled}
    error={args.hasError}
    name={args.field.name}
    onValueChange={args.onChange}
    options={(args.field.permittedValues ?? []).map(value => ({ value, label: value }))}
    required={args.required}
    value={args.strValue}
  />
)

export const DEFAULT_CONTROLS: ControlRegistry = {
  none: () => <></>,
  singlevalue: permittedChoiceControl,
  choicelist: permittedChoiceControl,
  radiogroup: args => (
    args.field.permittedValues?.length === 0 ? <></> : <RadioGroup
      disabled={args.disabled}
      error={args.hasError}
      name={args.field.name}
      onChange={args.onChange}
      options={(args.field.permittedValues ?? []).map(value => ({ value, label: value }))}
      required={args.required}
      value={args.strValue}
    />
  ),
  recordpicker: args => <DomainPickerControl args={args} />,
  taxonomypicker: args => <DomainPickerControl args={args} />,
  text: textControl,
  textarea: args => (
    <TextArea
      disabled={args.disabled}
      error={args.hasError}
      id={args.field.name}
      name={args.field.name}
      onChange={args.onChange}
      value={args.strValue}
    />
  ),
  number: numberControl,
  integer: numberControl,
  select: args => selectOptions(args).length === 0 ? textControl(args) : (
    <SelectField
      disabled={args.disabled}
      error={args.hasError}
      name={args.field.name}
      onValueChange={args.onChange}
      options={selectOptions(args)}
      required={args.required}
      value={args.strValue}
    />
  ),
  multiselect: args => <MultiSelectControl args={args} />,
  checkbox: args => <CheckControl args={args} />,
  boolean: args => <CheckControl args={args} />,
  'boolean-toggle': args => (
    <Switch
      checked={args.value === true}
      disabled={args.disabled}
      error={args.hasError}
      id={args.field.name}
      name={args.field.name}
      onCheckedChange={args.onChange}
      required={args.required}
    />
  ),
  date: args => (
    <DateField
      disabled={args.disabled}
      error={args.hasError}
      name={args.field.name}
      onChange={args.onChange}
      required={args.required}
      value={args.strValue}
    />
  ),
  datetime: args => (
    <DateTimeField
      disabled={args.disabled}
      error={args.hasError}
      name={args.field.name}
      onChange={args.onChange}
      required={args.required}
      value={args.strValue}
    />
  ),
  time: args => <NativeInputControl args={args} type="time" />,
  currency: args => <NumericControl args={args} kind="currency" />,
  percentage: args => <NumericControl args={args} kind="percentage" />,
  phone: args => (
    <TextBox
      disabled={args.disabled}
      error={args.hasError}
      id={args.field.name}
      inputMode="tel"
      name={args.field.name}
      onChange={args.onChange}
      type="tel"
      value={args.strValue}
    />
  ),
  email: args => (
    <TextBox
      autoComplete="email"
      disabled={args.disabled}
      error={args.hasError}
      id={args.field.name}
      inputMode="email"
      name={args.field.name}
      onChange={args.onChange}
      type="email"
      value={args.strValue}
    />
  ),
  url: args => (
    <TextBox
      autoComplete="url"
      disabled={args.disabled}
      error={args.hasError}
      id={args.field.name}
      inputMode="url"
      name={args.field.name}
      onChange={args.onChange}
      type="url"
      value={args.strValue}
    />
  ),
  readonly: args => <ReadonlyControl args={args} />,
  hidden: args => (
    <input
      name={args.field.name}
      readOnly
      type="hidden"
      value={typeof args.value === 'object' ? '' : args.strValue}
    />
  ),
}
