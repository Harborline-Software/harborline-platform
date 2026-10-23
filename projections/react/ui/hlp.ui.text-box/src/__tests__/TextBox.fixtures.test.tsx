import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { TextBox } from '../TextBox'
import { sharedCases } from './fixtures'

// Each neutral case runs the production component against its own input and expected values.
// targets-focus asserts keyboard reachability; the 44px target and visible focus ring are stylesheet
// behaviour left to the gallery's touch and focus-visible checks. projection-equivalence compares lanes.
type Case = { id: string; input: Record<string, any>; expected: Record<string, any> }
const cases = sharedCases as unknown as Case[]
const unasserted = ['text-box.projection-equivalence']
const input = () => document.querySelector('input')!
const action = (name: string) => document.querySelector<HTMLButtonElement>(`[data-hl-action="${name}"]`)

describe('TextBox shared fixture semantics', () => {
  it('covers every case or names it as unasserted', () => {
    expect(cases.length).toBeGreaterThan(0)
    for (const entry of cases) expect(entry.id in assertions || unasserted.includes(entry.id), entry.id).toBe(true)
  })
  for (const entry of cases.filter(entry => !unasserted.includes(entry.id))) {
    it(entry.id, async () => { await assertions[entry.id]!(entry) })
  }
})

const assertions: Record<string, (entry: Case) => void | Promise<void>> = {
  'text-box.defaults': ({ expected }) => {
    render(<TextBox aria-label="Query"/>)
    expect(input().value).toBe(expected.value)
    expect(input().type).toBe(expected.type)
    expect(action('clear') !== null).toBe(expected.clear)
    expect(action('reveal') !== null).toBe(expected.reveal)
    expect([input().dataset.hlSize, input().dataset.hlFill, input().dataset.hlRounded]).toEqual([expected.size, expected.fillMode, expected.rounded])
  },
  'text-box.controlled-change': ({ input: given, expected }) => {
    const requests: string[] = []
    render(<TextBox aria-label="Query" value={given.value} onChange={value => requests.push(value)}/>)
    fireEvent.change(input(), { target: { value: given.typed } })
    expect(requests).toEqual(expected.requests)
    expect(input().value).toBe(expected.visibleUntilReplacement)
  },
  'text-box.uncontrolled-default': ({ input: given, expected }) => {
    const requests: string[] = []
    render(<TextBox aria-label="Query" defaultValue={given.defaultValue} onChange={value => requests.push(value)}/>)
    fireEvent.change(input(), { target: { value: given.typed } })
    expect(requests).toEqual(expected.requests)
    expect(input().value).toBe(expected.visible)
  },
  'text-box.default-once': ({ input: given, expected }) => {
    const view = render(<TextBox aria-label="Query" defaultValue={given.initialDefault}/>)
    view.rerender(<TextBox aria-label="Query" defaultValue={given.laterDefault}/>)
    expect(input().value).toBe(expected.visible)
  },
  'text-box.external-replacement': ({ input: given, expected }) => {
    const view = render(<TextBox aria-label="Query" value={given.valueBefore} onChange={() => undefined}/>)
    view.rerender(<TextBox aria-label="Query" value={given.valueAfter} onChange={() => undefined}/>)
    expect(input().value).toBe(expected.visible)
    expect(input().value === given.valueBefore).toBe(expected.staleValue)
  },
  'text-box.clear-visible': ({ input: given, expected }) => {
    expect(given.values.map((value: string) => {
      const view = render(<TextBox aria-label="Query" value={value} clearButton={given.clearButton} disabled={given.disabled} onChange={() => undefined}/>)
      const visible = action('clear') !== null
      view.unmount()
      return visible
    })).toEqual(expected.visibleByValue)
  },
  'text-box.clear-action': ({ input: given, expected }) => {
    const requests: string[] = []
    render(<TextBox aria-label="Query" value={given.value} clearButton onChange={value => requests.push(value)}/>)
    action('clear')!.focus()
    fireEvent.click(action('clear')!)
    expect(requests).toEqual(expected.requests)
    expect(document.activeElement === input()).toBe(expected.focusWithinEditor)
  },
  'text-box.clear-disabled': ({ input: given, expected }) => {
    const requests: string[] = []
    render(<TextBox aria-label="Query" value={given.value} clearButton={given.clearButton} disabled={given.disabled} onChange={value => requests.push(value)}/>)
    expect(action('clear') !== null && !action('clear')!.disabled).toBe(expected.clearActionAvailable)
    // Attempt every route to a request: activate any clear control present and edit the input. The disabled
    // input itself blocks the edit (removing the component's disabled guard still passes), so the guard is
    // defence in depth and this counts what reaches the host.
    if (action('clear')) fireEvent.click(action('clear')!)
    fireEvent.change(input(), { target: { value: '' } })
    expect(requests).toHaveLength(expected.requests)
  },
  'text-box.password-reveal': ({ input: given, expected }) => {
    render(<TextBox aria-label="Secret" type={given.type} showReveal={given.showReveal} defaultValue="hunter2"/>)
    const types: string[] = []
    const names: string[] = []
    for (const _ of given.actions) { names.push(action('reveal')!.getAttribute('aria-label')!); fireEvent.click(action('reveal')!); types.push(input().type) }
    expect(types).toEqual(expected.types)
    expect(input().value === 'hunter2').toBe(expected.valueUnchanged)
    expect(names[0] === 'Show password' && names[1] === 'Hide password').toBe(expected.localizedNames)
  },
  'text-box.reveal-disabled': ({ input: given, expected }) => {
    render(<TextBox aria-label="Secret" type={given.type} showReveal={given.showReveal} disabled={given.disabled} defaultValue="hunter2"/>)
    expect(action('reveal') === null || action('reveal')!.disabled).toBe(expected.revealDisabledOrAbsent)
    expect(input().type).toBe(expected.type)
  },
  'text-box.form-context': ({ input: given, expected }) => {
    const context = given.context
    render(<FormFieldContext.Provider value={{ ...context, labelId: 'field-label' }}><TextBox/></FormFieldContext.Provider>)
    const applied = input().id === context.id && input().required === context.required && input().disabled === context.disabled
    expect(applied).toBe(expected.contextApplied)
    expect(input().getAttribute('aria-describedby') === context.describedBy && input().getAttribute('aria-labelledby') === 'field-label').toBe(expected.inputRelationship)
  },
  'text-box.validation': ({ input: given, expected }) => {
    render(<TextBox aria-label="Query" error={given.error} invalid={given.invalid} required={given.required}/>)
    expect(input().getAttribute('aria-invalid')).toBe(expected.ariaInvalid)
    expect(input().getAttribute('aria-required')).toBe(expected.ariaRequired)
    expect(input().dataset.hlInvalid === 'true').toBe(expected.visualInvalid)
  },
  'text-box.host-input-attributes': ({ input: given, expected }) => {
    render(<TextBox id={given.id} name={given.name} placeholder={given.placeholder} autoComplete={given.autocomplete} tabIndex={given.tabIndex} aria-label={given['aria-label']}/>)
    const onInput = input().id === given.id && input().name === given.name && input().placeholder === given.placeholder
      && input().getAttribute('autocomplete') === given.autocomplete && input().tabIndex === given.tabIndex && input().getAttribute('aria-label') === given['aria-label']
    expect(onInput).toBe(expected.allOnInput)
  },
  'text-box.keyboard-handler': ({ input: given, expected }) => {
    const caller = vi.fn()
    render(<TextBox aria-label="Query" onKeyDown={given.callerHandler ? caller : undefined}/>)
    fireEvent.keyDown(input(), { key: given.key })
    expect(caller).toHaveBeenCalledTimes(expected.callerCount)
    expect(caller.mock.calls.length > 1).toBe(expected.duplicate)
  },
  'text-box.prefix-suffix-actions': ({ input: given, expected }) => {
    render(<TextBox aria-label="Amount" value="12" onChange={() => undefined} prefix={given.prefix} suffix={given.suffix} clearButton={given.clearButton} showReveal={given.showReveal}/>)
    const root = input().closest('.hl-input')!
    const order: string[] = []
    root.querySelectorAll('input, .hl-input__prefix, .hl-text-box__caller-suffix, [data-hl-action]').forEach(node => {
      order.push(node.tagName === 'INPUT' ? 'input' : node.classList.contains('hl-input__prefix') ? 'prefix' : node.classList.contains('hl-text-box__caller-suffix') ? 'suffix' : (node as HTMLElement).dataset.hlAction!)
    })
    expect(order).toEqual(expected.order)
    expect(root.querySelector('.hl-text-box__caller-suffix')?.textContent === given.suffix).toBe(expected.suffixPreserved)
  },
  'text-box.locale-rtl-labels': ({ input: given, expected }) => {
    render(<HarborlineLocaleProvider locale={given.locale}><TextBox aria-label="Query" value="x" onChange={() => undefined} clearButton clearLabel={given.labels.clear}/></HarborlineLocaleProvider>)
    expect(input().getAttribute('dir')).toBe(expected.direction)
    expect(action('clear')!.getAttribute('aria-label')).toBe(expected.clearLabel)
    // Logical order: the action follows the input in DOM order inside an RTL context, so the browser places
    // it at the inline end; the rendered position is left to the gallery's RTL check.
    const follows = (input().compareDocumentPosition(action('clear')!) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0
    expect(follows && input().getAttribute('dir') === 'rtl' && action('clear')!.closest('.hl-text-box__suffix, .hl-input__suffix')?.closest('[dir]')?.getAttribute('dir') === 'rtl').toBe(expected.logicalOrder)
  },
  'text-box.targets-focus': async ({ input: given, expected }) => {
    render(<TextBox aria-label="Secret" type="password" defaultValue="hunter2" clearButton showReveal/>)
    expect(document.querySelectorAll('[data-hl-action]')).toHaveLength(given.actions)
    const user = userEvent.setup()
    input().focus()
    await user.tab()
    const clearReached = document.activeElement === action('clear')
    await user.tab()
    expect(clearReached && document.activeElement === action('reveal')).toBe(expected.actionsKeyboardReachable)
  },
  'text-box.appearance': ({ input: given, expected }) => {
    const canonical: Record<string, string> = { small: 'sm', medium: 'md', large: 'lg' }
    const sizes = given.sizes.map((size: string) => { const view = render(<TextBox aria-label="Query" size={size as never}/>); const token = input().dataset.hlSize; view.unmount(); return token })
    expect(sizes.every((token: string, index: number) => token === (canonical[given.sizes[index]] ?? given.sizes[index]))).toBe(expected.aliasesNormalized)
    const fills = given.fills.map((fill: string) => { const view = render(<TextBox aria-label="Query" fillMode={fill as never}/>); const token = input().dataset.hlFill; view.unmount(); return token })
    const rounding = given.rounding.map((value: string) => { const view = render(<TextBox aria-label="Query" rounded={value as never}/>); const token = input().dataset.hlRounded; view.unmount(); return token })
    expect(JSON.stringify(fills) === JSON.stringify(given.fills) && JSON.stringify(rounding) === JSON.stringify(given.rounding)).toBe(expected.allTokensStable)
  },
  'text-box.replacement': ({ input: given, expected }) => {
    const changed = vi.fn()
    const view = render(<TextBox aria-label="Query" value="v0" placeholder="p0" onChange={changed}/>)
    for (let index = 1; index <= given.updates; index++) view.rerender(<TextBox aria-label="Query" value={`v${index}`} placeholder={`p${index}`} onChange={changed}/>)
    expect(input().value === `v${given.updates}` && input().placeholder === `p${given.updates}`).toBe(expected.latestValueAndAttributesVisible)
    // No stale state: a single input whose value is the latest, never an earlier replacement.
    expect(Array.from(document.querySelectorAll('input')).filter(element => element.value !== `v${given.updates}`)).toHaveLength(expected.staleState)
    // Replacement itself requests nothing, and one edit afterwards requests exactly once.
    const replacementCalls = changed.mock.calls.length
    fireEvent.change(input(), { target: { value: 'typed' } })
    const editCalls = changed.mock.calls.length - replacementCalls
    expect(replacementCalls + Math.max(0, editCalls - 1)).toBe(expected.duplicateCallbacks)
    expect(editCalls).toBe(1)
  },
}
