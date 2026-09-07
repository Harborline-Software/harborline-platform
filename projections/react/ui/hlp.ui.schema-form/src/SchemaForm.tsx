import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'
import { FormField } from '@harborline-platform/hlp.ui.form-field'
import {
  resolveText,
  type ContentNode,
  type FormActionConfig,
  type FormValues,
  type FormViewField,
  type FormViewItem,
  type FormViewSection,
  type PresentationOutcome,
  type ValidationResult,
} from '@harborline-platform/hlp.ui.form-view'

import { controlAcceptsValue, DEFAULT_CONTROLS } from './controls'
import type {
  ControlArgs,
  ControlRegistry,
  SchemaFormProps,
  SchemaFormStrings,
} from './SchemaForm.types'
import { useFormRuleGraph } from './useFormRuleGraph'

const DEFAULT_STRINGS: SchemaFormStrings = {
  submit: 'Submit',
  submitting: 'Submitting…',
  errorSummaryTitle: 'Please fix the following errors',
  redacted: 'Hidden',
  empty: 'This form has no fields to display.',
  addItem: 'Add',
  removeItem: 'Remove',
  itemLabel: 'Item {n}',
  controlUnavailable: 'Control unavailable: {control}',
  submitBlocked: 'Submission is unavailable while rules are pending or errored.',
}

const DEFAULT_LOCALE_CHAIN: readonly string[] = []
const BUILT_IN_HINTS = new Set(Object.keys(DEFAULT_CONTROLS))
const RTL_PRIMARY_SUBTAGS = new Set(['ar', 'fa', 'he', 'ps', 'ur'])

function getIn(root: unknown, path: readonly (string | number)[]): unknown {
  let current = root
  for (const segment of path) {
    if (current === null || typeof current !== 'object') return undefined
    current = (current as Record<string | number, unknown>)[segment]
  }
  return current
}

function setIn(node: unknown, path: readonly (string | number)[], value: unknown): unknown {
  if (path.length === 0) return value
  const [head, ...rest] = path
  if (typeof head === 'number') {
    const array = Array.isArray(node) ? [...node] : []
    array[head] = setIn(array[head], rest, value)
    return array
  }
  const object = node && typeof node === 'object' && !Array.isArray(node)
    ? { ...(node as Record<string, unknown>) }
    : {}
  object[head] = setIn(object[head], rest, value)
  return object
}

type ValuePath = readonly (string | number)[]

function pathKey(path: ValuePath): string {
  return JSON.stringify(path)
}

class FormValueStore {
  private values: FormValues
  private readonly subscriptions = new Map<string, { path: ValuePath; listeners: Set<() => void> }>()

  constructor(values: FormValues) {
    this.values = values
  }

  get(path: ValuePath): unknown {
    return getIn(this.values, path)
  }

  replace(values: FormValues): void {
    if (values === this.values) return
    const previous = this.values
    this.values = values
    for (const { path, listeners } of this.subscriptions.values()) {
      if (Object.is(getIn(previous, path), getIn(values, path))) continue
      for (const listener of listeners) listener()
    }
  }

  replaceAtPath(values: FormValues, changedPath: ValuePath): void {
    if (values === this.values) return
    const previous = this.values
    this.values = values
    for (let length = 1; length <= changedPath.length; length += 1) {
      const path = changedPath.slice(0, length)
      const subscription = this.subscriptions.get(pathKey(path))
      if (!subscription || Object.is(getIn(previous, path), getIn(values, path))) continue
      for (const listener of subscription.listeners) listener()
    }
  }

  subscribe(path: ValuePath, listener: () => void): () => void {
    const key = pathKey(path)
    const existing = this.subscriptions.get(key)
    const subscription = existing ?? { path: [...path], listeners: new Set() }
    if (!existing) this.subscriptions.set(key, subscription)
    subscription.listeners.add(listener)
    return () => {
      subscription.listeners.delete(listener)
      if (subscription.listeners.size === 0) this.subscriptions.delete(key)
    }
  }
}

const FormValueStoreContext = React.createContext<FormValueStore | null>(null)

function useValueAtPath(path: ValuePath): unknown {
  const store = React.useContext(FormValueStoreContext)
  if (!store) throw new Error('schema-form-value-store-missing')
  const subscribe = React.useCallback((listener: () => void) => store.subscribe(path, listener), [path, store])
  const getSnapshot = React.useCallback(() => store.get(path), [path, store])
  return React.useSyncExternalStore(subscribe, getSnapshot, getSnapshot)
}

function partitionErrors(result: ValidationResult | null): {
  byField: Record<string, string>
  formLevel: string[]
} {
  const byField: Record<string, string> = {}
  const formLevel: string[] = []
  if (!result || result.isValid) return { byField, formLevel }
  for (const error of result.errors) {
    const match = /^\/([^/]+)$/.exec(error.jsonPointer)
    if (!match) {
      formLevel.push(error.message)
      continue
    }
    const field = match[1]!.replace(/~1/g, '/').replace(/~0/g, '~')
    if (byField[field] === undefined) byField[field] = error.message
    else formLevel.push(error.message)
  }
  return { byField, formLevel }
}

function unavailableControl(args: ControlArgs, hint: string): React.ReactElement {
  return (
    <output
      aria-disabled="true"
      className="hl-schema-form__unavailable"
      data-error-code="schema-form.unavailable-value"
      id={args.field.name}
    >
      {args.strings.controlUnavailable.replace('{control}', hint)}
    </output>
  )
}

function renderControl(args: ControlArgs, registry: ControlRegistry): React.ReactElement {
  const hint = (args.field.controlHint ?? 'text').toLocaleLowerCase()
  const renderer = registry[hint]
  if (renderer) {
    if (BUILT_IN_HINTS.has(hint) && !controlAcceptsValue(hint, args.value)) {
      return unavailableControl(args, hint)
    }
    return renderer(args)
  }
  const structured = args.value !== null && typeof args.value === 'object'
  if (structured || (args.field.valueKind !== undefined && args.field.valueKind !== 'string')) {
    return unavailableControl(args, hint)
  }
  return registry.text(args)
}

function Presentation({ value, chain }: { value: PresentationOutcome; chain: readonly string[] }) {
  const badge = resolveText(value.badge, chain)
  if (!badge) return null
  return (
    <span
      className="hl-schema-form__presentation"
      data-severity={value.severity ?? 'info'}
      data-style-token={value.styleToken}
    >
      {badge}
    </span>
  )
}

interface SchemaFieldProps {
  field: FormViewField
  chain: readonly string[]
  path: readonly (string | number)[]
  error?: string
  disabled: boolean
  strings: SchemaFormStrings
  onSet: (path: readonly (string | number)[], value: unknown) => void
}

function equalPath(left: readonly (string | number)[], right: readonly (string | number)[]): boolean {
  return left.length === right.length && left.every((segment, index) => segment === right[index])
}

function equalSchemaFieldProps(previous: SchemaFieldProps, next: SchemaFieldProps): boolean {
  return previous.field === next.field
    && previous.chain === next.chain
    && equalPath(previous.path, next.path)
    && previous.error === next.error
    && previous.disabled === next.disabled
    && previous.strings === next.strings
    && previous.onSet === next.onSet
}

const SchemaField = React.memo(function SchemaField({
  field,
  chain,
  path,
  error,
  disabled,
  strings,
  onSet,
}: SchemaFieldProps) {
  const value = useValueAtPath(path)
  const label = resolveText(field.label, chain)
  const hint = resolveText(field.helpText, chain) || undefined
  const required = field.required ?? false
  const controlHint = (field.controlHint ?? 'text').toLocaleLowerCase()
  const registry = React.useContext(ControlRegistryContext)
  const strValue = value === null || value === undefined ? '' : String(value)
  const onChange = React.useCallback((next: unknown) => onSet(path, next), [onSet, path])

  if (controlHint === 'hidden') {
    return renderControl({
      field, chain, value, strValue, hasError: false, required, disabled, strings, onChange,
    }, registry)
  }

  if (field.isSensitive || !field.isReadable) {
    return (
      <FormField disabled hint={hint} label={label} name={field.name}>
        <output aria-disabled="true" className="hl-schema-form__readonly" id={field.name}>
          {strings.redacted}
        </output>
      </FormField>
    )
  }

  const effectiveDisabled = disabled || Boolean(field.readOnly)
  const control = renderControl({
    field,
    chain,
    value,
    strValue,
    hasError: Boolean(error),
    required,
    disabled: effectiveDisabled,
    strings,
    onChange,
  }, registry)

  return (
    <FormField
      data-presentation-severity={field.presentation?.severity ?? undefined}
      data-presentation-style-token={field.presentation?.styleToken}
      data-read-only={field.readOnly || undefined}
      error={error}
      hint={hint}
      label={label}
      name={field.name}
      required={required}
    >
      {control}
      {field.presentation ? <Presentation chain={chain} value={field.presentation} /> : null}
    </FormField>
  )
}, equalSchemaFieldProps)

function ContentBlock({ content, chain }: { content: readonly ContentNode[]; chain: readonly string[] }) {
  return (
    <div className="hl-schema-form__content">
      {content.map((node, index) => node.kind === 'heading'
        ? <h3 key={index}>{resolveText(node.text, chain)}</h3>
        : <p key={index}>{resolveText(node.text, chain)}</p>)}
    </div>
  )
}

interface SchemaItemsProps {
  items: readonly FormViewItem[]
  path: readonly (string | number)[]
  chain: readonly string[]
  disabled: boolean
  strings: SchemaFormStrings
  onSet: (path: readonly (string | number)[], value: unknown) => void
  onAction: (action: FormActionConfig) => void
}

function SchemaItems({ items, path, chain, disabled, strings, onSet, onAction }: SchemaItemsProps) {
  return (
    <div className="hl-schema-form__items">
      {items.map(item => {
        if (item.kind === 'field') {
          const fieldPath = [...path, item.field.name]
          return (
            <SchemaField
              chain={chain}
              disabled={disabled}
              field={item.field}
              key={item.key}
              onSet={onSet}
              path={fieldPath}
              strings={strings}
            />
          )
        }
        if (item.kind === 'content') return <ContentBlock chain={chain} content={item.content} key={item.key} />
        if (item.kind === 'action') {
          return (
            <button
              className="hl-schema-form__secondary-action"
              disabled={disabled}
              key={item.key}
              onClick={() => onAction(item.action)}
              type="button"
            >
              {resolveText(item.action.label, chain)}
            </button>
          )
        }
        if (item.kind === 'group') {
          return (
            <fieldset className="hl-schema-form__group" data-testid={`group-${item.key}`} key={item.key}>
              {item.title ? <legend>{resolveText(item.title, chain)}</legend> : null}
              <SchemaItems
                chain={chain}
                disabled={disabled}
                items={item.items}
                onAction={onAction}
                onSet={onSet}
                path={[...path, item.key]}
                strings={strings}
              />
            </fieldset>
          )
        }
        return (
          <CollectionField
            chain={chain}
            disabled={disabled}
            item={item}
            key={item.key}
            onAction={onAction}
            onSet={onSet}
            path={path}
            strings={strings}
          />
        )
      })}
    </div>
  )
}

interface CollectionFieldProps extends Omit<SchemaItemsProps, 'items'> {
  item: Extract<FormViewItem, { kind: 'collection' }>
}

function CollectionField({ item, path, chain, disabled, strings, onSet, onAction }: CollectionFieldProps) {
  const collectionPath = [...path, item.key]
  const current = useValueAtPath(collectionPath)
  const rows = Array.isArray(current) ? current : []
  const minimum = item.cardinality?.min ?? 0
  const maximum = item.cardinality?.max ?? null
  const canAdd = maximum === null || maximum === undefined || rows.length < maximum
  const canRemove = rows.length > minimum
  const rowKeys = React.useRef<string[]>([])
  const sequence = React.useRef(0)
  while (rowKeys.current.length < rows.length) rowKeys.current.push(`row-${sequence.current += 1}`)
  if (rowKeys.current.length > rows.length) rowKeys.current.length = rows.length

  const add = () => {
    if (disabled || !canAdd) return
    rowKeys.current.push(`row-${sequence.current += 1}`)
    onSet(collectionPath, [...rows, {}])
  }
  const remove = (index: number) => {
    if (disabled || !canRemove) return
    rowKeys.current.splice(index, 1)
    onSet(collectionPath, rows.filter((_, candidate) => candidate !== index))
  }

  return (
    <fieldset className="hl-schema-form__collection" data-testid={`collection-${item.key}`}>
      {item.title ? <legend>{resolveText(item.title, chain)}</legend> : null}
      <div className="hl-schema-form__collection-rows">
        {rows.map((_, index) => {
          const itemText = strings.itemLabel.replace('{n}', String(index + 1))
          return (
            <fieldset
              className="hl-schema-form__collection-row"
              data-testid={`collection-${item.key}-instance`}
              key={rowKeys.current[index]}
            >
              <legend>{itemText}</legend>
              <button
                aria-label={`${strings.removeItem} ${itemText}`}
                className="hl-schema-form__remove"
                disabled={disabled || !canRemove}
                onClick={() => remove(index)}
                type="button"
              >
                {strings.removeItem}
              </button>
              <SchemaItems
                chain={chain}
                disabled={disabled}
                items={item.items}
                onAction={onAction}
                onSet={onSet}
                path={[...collectionPath, index]}
                strings={strings}
              />
            </fieldset>
          )
        })}
      </div>
      <button
        className="hl-schema-form__add"
        data-testid={`collection-${item.key}-add`}
        disabled={disabled || !canAdd}
        onClick={add}
        type="button"
      >
        {strings.addItem}
      </button>
    </fieldset>
  )
}

const ControlRegistryContext = React.createContext<ControlRegistry>(DEFAULT_CONTROLS)

interface SchemaSectionProps {
  section: FormViewSection
  chain: readonly string[]
  disabled: boolean
  strings: SchemaFormStrings
  byField: Readonly<Record<string, string>>
  onSet: (path: ValuePath, value: unknown) => void
  onAction: (action: FormActionConfig) => void
  setSectionRef: (id: string, element: HTMLFieldSetElement | null) => void
}

const SchemaSection = React.memo(function SchemaSection({
  section,
  chain,
  disabled,
  strings,
  byField,
  onSet,
  onAction,
  setSectionRef,
}: SchemaSectionProps) {
  return (
    <fieldset
      className="hl-schema-form__section"
      data-schemaform-section={section.id}
      ref={element => setSectionRef(section.id, element)}
    >
      <legend>{resolveText(section.title, chain)}</legend>
      {section.items && section.items.length > 0 ? (
        <SchemaItems
          chain={chain}
          disabled={disabled}
          items={section.items}
          onAction={onAction}
          onSet={onSet}
          path={[]}
          strings={strings}
        />
      ) : (
        <div className="hl-schema-form__fields">
          {section.fields.map(field => (
            <SchemaField
              chain={chain}
              disabled={disabled}
              error={byField[field.name]}
              field={field}
              key={field.name}
              onSet={onSet}
              path={[field.name]}
              strings={strings}
            />
          ))}
        </div>
      )}
    </fieldset>
  )
})

function summaryErrors(formLevel: readonly string[], byField: Readonly<Record<string, string>>): string[] {
  return formLevel.length > 0 ? [...formLevel] : Object.values(byField)
}

export function SchemaForm({
  view: baseView,
  ruleGraph = null,
  initialValues,
  values: controlledValues,
  onSubmit,
  onChange,
  onValuesChange,
  strings,
  localeChain = DEFAULT_LOCALE_CHAIN,
  className,
  disabled = false,
  controls,
  onBlockAction,
}: SchemaFormProps) {
  const copy = React.useMemo(() => ({ ...DEFAULT_STRINGS, ...strings }), [strings])
  const registry = React.useMemo<ControlRegistry>(() => ({
    ...DEFAULT_CONTROLS,
    ...controls,
    text: controls?.text ?? DEFAULT_CONTROLS.text,
  }), [controls])
  const projected = useFormRuleGraph(ruleGraph, baseView, {
    initialValues,
    values: controlledValues,
    onChange,
    onValuesChange,
  })
  const { view, values, setValues, saveBlocked } = projected
  const valueStoreRef = React.useRef<FormValueStore | null>(null)
  if (valueStoreRef.current === null) valueStoreRef.current = new FormValueStore(values)
  const valueStore = valueStoreRef.current
  const valuesRef = React.useRef(values)
  valuesRef.current = values
  const [validation, setValidation] = React.useState<ValidationResult | null>(null)
  const [submitting, setSubmitting] = React.useState(false)
  const [blockedAttempt, setBlockedAttempt] = React.useState(false)
  const formRef = React.useRef<HTMLFormElement>(null)
  const summaryRef = React.useRef<HTMLDivElement>(null)
  const submitRef = React.useRef<HTMLButtonElement>(null)
  const emptyRef = React.useRef<HTMLParagraphElement>(null)
  const focusBeforeChange = React.useRef<HTMLElement | null>(null)
  const sectionRefs = React.useRef<Record<string, HTMLFieldSetElement | null>>({})

  const { byField, formLevel } = React.useMemo(() => partitionErrors(validation), [validation])

  React.useLayoutEffect(() => {
    valueStore.replace(values)
  }, [valueStore, values])

  React.useEffect(() => {
    if ((blockedAttempt || (validation && !validation.isValid)) && summaryRef.current) {
      summaryRef.current.focus()
    }
  }, [blockedAttempt, validation])

  React.useEffect(() => {
    const previous = focusBeforeChange.current
    focusBeforeChange.current = null
    if (previous && !previous.isConnected) (submitRef.current ?? emptyRef.current)?.focus()
  }, [view])

  React.useEffect(() => {
    if (!saveBlocked) setBlockedAttempt(false)
  }, [saveBlocked])

  const commit = React.useCallback((next: FormValues, changedPath: ValuePath) => {
    const active = document.activeElement
    focusBeforeChange.current = active instanceof HTMLElement && formRef.current?.contains(active) ? active : null
    if (controlledValues === undefined) {
      valuesRef.current = next
      valueStore.replaceAtPath(next, changedPath)
    }
    setValues(next)
  }, [controlledValues, setValues, valueStore])

  const setValueAtPath = React.useCallback((path: readonly (string | number)[], value: unknown) => {
    commit(setIn(valuesRef.current, path, value) as FormValues, path)
  }, [commit])

  const handleSubmit = React.useCallback(async () => {
    if (disabled || submitting) return
    if (saveBlocked) {
      setBlockedAttempt(true)
      return
    }
    setBlockedAttempt(false)
    setSubmitting(true)
    try {
      const result = await onSubmit(valuesRef.current)
      setValidation(result && typeof result === 'object' ? result : null)
    } finally {
      setSubmitting(false)
    }
  }, [disabled, onSubmit, saveBlocked, submitting])

  const handleAction = React.useCallback((action: FormActionConfig) => {
    if (action.kind === 'scroll-to-section') {
      const section = action.sectionId ? sectionRefs.current[action.sectionId] : null
      if (section) {
        section.scrollIntoView({ block: 'start' })
        section.tabIndex = -1
        section.focus({ preventScroll: true })
      }
      return
    }
    onBlockAction?.(action)
  }, [onBlockAction])

  const setSectionRef = React.useCallback((id: string, element: HTMLFieldSetElement | null) => {
    sectionRefs.current[id] = element
  }, [])

  const visibleSections = view.sections.filter(section => section.fields.length > 0 || (section.items?.length ?? 0) > 0)
  const title = resolveText(view.title, localeChain)
  const description = resolveText(view.description, localeChain)
  const validationMessages = summaryErrors(formLevel, byField)

  return (
    <ControlRegistryContext.Provider value={registry}>
      <FormValueStoreContext.Provider value={valueStore}>
        <form
          aria-label={title || undefined}
          className={cn('hl-schema-form', className)}
          dir={RTL_PRIMARY_SUBTAGS.has(localeChain[0]?.split('-')[0]?.toLocaleLowerCase() ?? '') ? 'rtl' : undefined}
          noValidate
          onSubmit={event => {
            event.preventDefault()
            void handleSubmit()
          }}
          ref={formRef}
        >
        {description ? <p className="hl-schema-form__description">{description}</p> : null}

        {blockedAttempt || (validation && !validation.isValid) ? (
          <div
            className="hl-schema-form__summary"
            data-error-code={blockedAttempt ? 'schema-form.submit-blocked' : undefined}
            ref={summaryRef}
            role="alert"
            tabIndex={-1}
          >
            <h2>{copy.errorSummaryTitle}</h2>
            {blockedAttempt ? <p>{copy.submitBlocked}</p> : (
              <ul>{validationMessages.map((message, index) => <li key={`${index}:${message}`}>{message}</li>)}</ul>
            )}
          </div>
        ) : null}

        {visibleSections.length === 0 ? (
          <p className="hl-schema-form__empty" ref={emptyRef} tabIndex={-1}>{copy.empty}</p>
        ) : (
          visibleSections.map(section => (
            <SchemaSection
              byField={byField}
              chain={localeChain}
              disabled={disabled || submitting}
              key={section.id}
              onAction={handleAction}
              onSet={setValueAtPath}
              section={section}
              setSectionRef={setSectionRef}
              strings={copy}
            />
          ))
        )}

        {visibleSections.length > 0 ? (
          <button
            className="hl-schema-form__submit"
            disabled={disabled || submitting || saveBlocked}
            ref={submitRef}
            type="submit"
          >
            {submitting ? copy.submitting : copy.submit}
          </button>
        ) : null}
        </form>
      </FormValueStoreContext.Provider>
    </ControlRegistryContext.Provider>
  )
}

export type { ControlArgs, ControlRegistry, SchemaFormProps, SchemaFormStrings } from './SchemaForm.types'
export type { FormValues, FormView, ValidationError, ValidationResult } from '@harborline-platform/hlp.ui.form-view'
