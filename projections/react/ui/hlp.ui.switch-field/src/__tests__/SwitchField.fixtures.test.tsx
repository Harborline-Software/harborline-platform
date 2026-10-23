import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { SwitchField } from '../SwitchField'
import { sharedCases } from './fixtures'

// Each neutral case runs the production component against its own input and expected values.
// appearance asserts the size tokens and logical direction reach the shared Switch; the 44px target is
// stylesheet behaviour left to the gallery's touch check. projection-equivalence compares lanes.
type Case = { id: string; input: Record<string, any>; expected: Record<string, any> }
const cases = sharedCases as unknown as Case[]
const unasserted = ['switch-field.projection-equivalence']
const control = () => screen.getByRole('switch')

describe('SwitchField shared fixture semantics', () => {
  it('covers every case or names it as unasserted', () => {
    expect(cases.length).toBeGreaterThan(0)
    for (const entry of cases) expect(entry.id in assertions || unasserted.includes(entry.id), entry.id).toBe(true)
  })
  for (const entry of cases.filter(entry => !unasserted.includes(entry.id))) {
    it(entry.id, async () => { await assertions[entry.id]!(entry) })
  }
})

const assertions: Record<string, (entry: Case) => void | Promise<void>> = {
  'switch-field.defaults': ({ input, expected }) => {
    const changed = vi.fn()
    render(<SwitchField aria-label="Published" checked={input.checked} name={input.name} onCheckedChange={changed}/>)
    expect(control().getAttribute('role')).toBe(expected.role)
    expect(control().getAttribute('aria-checked')).toBe(expected.ariaChecked)
    fireEvent.click(control())
    // Controlled: the request is made and the rendered state waits for the caller.
    expect(changed).toHaveBeenCalledWith(true)
    expect(control().getAttribute('aria-checked') === expected.ariaChecked).toBe(expected.controlled)
  },
  'switch-field.id-fallback': ({ input, expected }) => {
    render(<SwitchField aria-label="Published" checked={false} name={input.name} onCheckedChange={() => undefined}/>)
    expect(control().id).toBe(expected.id)
    expect(document.querySelector<HTMLInputElement>('input[type="hidden"]')?.name).toBe(expected.name)
  },
  'switch-field.checked': ({ input, expected }) => {
    render(<SwitchField aria-label="Published" checked={input.checked} name="published" onCheckedChange={() => undefined}/>)
    expect(control().getAttribute('aria-checked')).toBe(expected.ariaChecked)
    expect(control().dataset.hlChecked === 'true' ? 'checked' : 'unchecked').toBe(expected.thumbState)
  },
  'switch-field.pointer': async ({ input, expected }) => {
    const requests: boolean[] = []
    render(<SwitchField aria-label="Published" checked={input.checked} name="published" onCheckedChange={value => requests.push(value)}/>)
    await userEvent.setup().click(control())
    expect(requests).toEqual(expected.requests)
  },
  'switch-field.keyboard': async ({ input, expected }) => {
    const user = userEvent.setup()
    const requests: boolean[] = []
    render(<SwitchField aria-label="Published" checked={input.checked} name="published" onCheckedChange={value => requests.push(value)}/>)
    control().focus()
    const perKey = (input.keys as string[]).map(() => 0)
    for (const [index, key] of (input.keys as string[]).entries()) {
      const before = requests.length
      await user.keyboard(key === 'Space' ? '[Space]' : `[${key}]`)
      perKey[index] = requests.length - before
    }
    expect(perKey.every(count => count >= 1)).toBe(expected.eachRequests)
    expect(perKey.some(count => count > 1)).toBe(expected.duplicateRequests)
    expect(document.activeElement).toBe(control())
  },
  'switch-field.disabled': async ({ input, expected }) => {
    const user = userEvent.setup()
    const changed = vi.fn()
    render(<SwitchField aria-label="Published" checked={false} disabled={input.disabled} name="published" onCheckedChange={changed}/>)
    expect(control().hasAttribute('disabled')).toBe(expected.disabled)
    for (const action of input.actions as string[]) {
      if (action === 'click') await user.click(control())
      else { control().focus(); await user.keyboard(action === 'Space' ? '[Space]' : `[${action}]`) }
    }
    expect(changed).toHaveBeenCalledTimes(expected.requests)
  },
  'switch-field.label': ({ input, expected }) => {
    render(<SwitchField checked={false} label={input.label} name="published" onCheckedChange={() => undefined}/>)
    expect(screen.getByRole('switch', { name: expected.accessibleName })).toBe(control())
    expect(screen.queryByText(input.label) !== null).toBe(expected.visibleLabel)
  },
  'switch-field.form-context': ({ input, expected }) => {
    const context = input.context
    const changed = vi.fn()
    render(
      <FormFieldContext.Provider value={{ id: 'ambient', labelId: 'field-label', describedBy: context.descriptionId, required: context.required, disabled: context.disabled }}>
        <span id="field-label">{context.label}</span>
        <SwitchField checked={false} name="enabled" onCheckedChange={changed}/>
      </FormFieldContext.Provider>,
    )
    const merged = screen.getByRole('switch', { name: context.label }) === control()
      && control().getAttribute('aria-required') === String(context.required) && control().hasAttribute('disabled') === context.disabled
    fireEvent.click(control())
    expect(merged && changed.mock.calls.length === 0).toBe(expected.contextMerged)
    expect(control().getAttribute('aria-describedby')).toBe(expected.describedBy)
  },
  'switch-field.validation': ({ input, expected }) => {
    render(<SwitchField aria-label="Published" checked={false} error={input.invalid} name="published" onCheckedChange={() => undefined} required={input.required}/>)
    expect(control().getAttribute('aria-required')).toBe(expected.ariaRequired)
    expect(control().getAttribute('aria-invalid')).toBe(expected.ariaInvalid)
  },
  'switch-field.appearance': ({ input, expected }) => {
    const sizes = (input.sizes as string[]).map(size => {
      const view = render(<HarborlineLocaleProvider locale="ar-SA"><SwitchField aria-label="Published" checked={false} name="published" onCheckedChange={() => undefined} size={size as never}/></HarborlineLocaleProvider>)
      const root = control().closest('.hl-switch')!
      const result = { size: root.getAttribute('data-hl-size'), dir: root.getAttribute('dir'), thumb: root.querySelector('.hl-switch__thumb') !== null }
      view.unmount()
      return result
    })
    expect(sizes.every((value, index) => value.size === input.sizes[index] && value.thumb)).toBe(expected.delegatedToSwitch)
    expect(sizes.every(value => value.dir === input.direction)).toBe(expected.logicalThumb)
  },
  'switch-field.replacement': ({ input, expected }) => {
    const changed = vi.fn()
    const view = render(<SwitchField aria-label="Published" checked={false} name="published" onCheckedChange={changed}/>)
    for (let index = 1; index <= input.updates; index++) view.rerender(<SwitchField aria-label="Published" checked={index % 2 === 1} name="published" onCheckedChange={changed}/>)
    const latest = input.updates % 2 === 1
    expect(control().getAttribute('aria-checked') === String(latest)).toBe(expected.latestCheckedVisible)
    expect(document.querySelectorAll('[role="switch"]').length - 1).toBe(expected.staleState)
    // Replacement itself requests nothing, and one activation afterwards requests exactly once.
    const replacementCalls = changed.mock.calls.length
    fireEvent.click(control())
    const activationCalls = changed.mock.calls.length - replacementCalls
    expect(replacementCalls + Math.max(0, activationCalls - 1)).toBe(expected.duplicateCallbacks)
    expect(activationCalls).toBe(1)
  },
}
