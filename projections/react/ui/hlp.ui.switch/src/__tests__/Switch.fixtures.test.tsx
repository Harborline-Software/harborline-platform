import type * as React from 'react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { Switch } from '../Switch'
import { sharedCases } from './fixtures'

// Each neutral case runs the production component against its own input and expected values.
// touch-target asserts the coarse-pointer touch marker, the unchanged size token, and the stylesheet's
// 44px rule keyed on that marker; rtl asserts logical direction and checked markers (pixel geometry is
// the gallery's check). projection-equivalence compares lanes.
type Case = { id: string; input: Record<string, any>; expected: Record<string, any> }
const cases = sharedCases as unknown as Case[]
const unasserted = ['switch.projection-equivalence']
const control = () => screen.getByRole('switch')
const defaultMatchMedia = window.matchMedia
const setMatchMedia = (value: typeof window.matchMedia) => Object.defineProperty(window, 'matchMedia', { configurable: true, value })
afterEach(() => { setMatchMedia(defaultMatchMedia) })

describe('Switch shared fixture semantics', () => {
  it('covers every case or names it as unasserted', () => {
    expect(cases.length).toBeGreaterThan(0)
    for (const entry of cases) expect(entry.id in assertions || unasserted.includes(entry.id), entry.id).toBe(true)
  })
  for (const entry of cases.filter(entry => !unasserted.includes(entry.id))) {
    it(entry.id, async () => { await assertions[entry.id]!(entry) })
  }
})

const assertions: Record<string, (entry: Case) => void | Promise<void>> = {
  'switch.uncontrolled': async ({ input, expected }) => {
    const user = userEvent.setup()
    const requests: boolean[] = []
    render(<Switch accessibleName="Email alerts" defaultChecked={input.defaultChecked} onCheckedChange={value => requests.push(value)}/>)
    const states = [control().getAttribute('aria-checked') === 'true']
    for (let index = 0; index < input.activations; index++) {
      await user.click(control())
      states.push(control().getAttribute('aria-checked') === 'true')
    }
    expect(states).toEqual(expected.states)
    expect(requests).toEqual(expected.requestedValues)
  },
  'switch.controlled': ({ input, expected }) => {
    const requests: boolean[] = []
    render(<Switch accessibleName="Email alerts" checked={input.checked} onCheckedChange={value => requests.push(value)}/>)
    if (input.activate) fireEvent.click(control())
    // Controlled: the request is made and the rendered state waits for the caller.
    expect(control().getAttribute('aria-checked') === 'true').toBe(expected.renderedChecked)
    expect(requests).toEqual(expected.requestedValues)
  },
  'switch.pointer': async ({ input, expected }) => {
    const requests: boolean[] = []
    render(<Switch accessibleName="Email alerts" checked={input.checked} onCheckedChange={value => requests.push(value)}/>)
    if (input.pointer === 'click') await userEvent.setup().click(control())
    expect(requests).toEqual(expected.requestedValues)
    expect(requests.length).toBe(expected.requestCount)
  },
  'switch.keyboard': async ({ input, expected }) => {
    const user = userEvent.setup()
    const requests: boolean[] = []
    render(<Switch accessibleName="Email alerts" checked={input.checked} onCheckedChange={value => requests.push(value)}/>)
    control().focus()
    const perKey: number[] = []
    for (const key of input.keys as string[]) {
      const before = requests.length
      await user.keyboard(`[${key}]`)
      perKey.push(requests.length - before)
    }
    expect(requests).toEqual(expected.requestedValues)
    expect(perKey.every(count => count === 1)).toBe(expected.oneRequestPerKey)
  },
  'switch.disabled': async ({ input, expected }) => {
    const user = userEvent.setup()
    const changed = vi.fn()
    render(<Switch accessibleName="Email alerts" disabled={input.disabled} onCheckedChange={changed}/>)
    expect(control().hasAttribute('disabled')).toBe(expected.nativeDisabled)
    for (const method of input.methods as string[]) {
      if (method === 'pointer') await user.click(control())
      else { control().focus(); await user.keyboard(`[${method}]`) }
    }
    expect(changed).toHaveBeenCalledTimes(expected.requestCount)
  },
  'switch.stable-name': ({ input, expected }) => {
    const states = input.states as boolean[]
    const view = render(<Switch accessibleName={input.accessibleName} checked={states[0]}/>)
    const checkedStates = states.map((checked, index) => {
      view.rerender(<Switch accessibleName={input.accessibleName} checked={checked}/>)
      expect(screen.getByRole('switch', { name: expected.names[index] })).toBe(control())
      return control().getAttribute('aria-checked') === 'true'
    })
    expect(checkedStates).toEqual(expected.checkedStates)
  },
  'switch.visible-label': async ({ input, expected }) => {
    const requests: boolean[] = []
    render(<Switch checked={false} label={input.label} onCheckedChange={value => requests.push(value)}/>)
    const label = screen.getByText(input.label)
    const associated = label.getAttribute('for') === control().id && screen.getByRole('switch', { name: input.label }) === control()
    expect(associated).toBe(expected.associated)
    if (input.activateLabel) await userEvent.setup().click(label)
    expect(requests).toEqual(expected.requestedValues)
  },
  'switch.state-copy': ({ input, expected }) => {
    const states = input.states as boolean[]
    const view = render(<Switch accessibleName="Alerts" checked={states[0]} offText={input.offText} onText={input.onText}/>)
    const names: string[] = []
    const visibleText = states.map(checked => {
      view.rerender(<Switch accessibleName="Alerts" checked={checked} offText={input.offText} onText={input.onText}/>)
      names.push(control().getAttribute('aria-label') ?? '')
      return view.container.querySelector('.hl-switch__state')!.textContent
    })
    expect(visibleText).toEqual(expected.visibleText)
    // The state copy is aria-hidden and never becomes the name.
    expect(names.every(name => name === 'Alerts') && screen.queryByRole('switch', { name: /Enabled|Disabled/ }) === null).toBe(expected.accessibleNameUnchanged)
  },
  'switch.description': ({ input, expected }) => {
    render(<Switch description={input.description} label={input.label}/>)
    const description = screen.getByText(input.description)
    expect(description.closest('[aria-hidden="true"]') === null).toBe(expected.descriptionVisible)
    expect((control().getAttribute('aria-describedby') ?? '').split(' ').includes(description.id)).toBe(expected.describedByIncludesDescription)
  },
  'switch.form-field': ({ input, expected }) => {
    const field = (child: React.ReactElement) => (
      <FormFieldContext.Provider value={{ id: 'ambient', labelId: 'field-label', describedBy: `${input.hintId} ${input.errorId}`, required: input.required, disabled: input.disabled }}>
        <span id="field-label">{input.label}</span>{child}
      </FormFieldContext.Provider>
    )
    const ambient = render(field(<Switch/>))
    const applied = screen.getByRole('switch', { name: input.label }) === control()
      && control().getAttribute('aria-required') === String(input.required) && control().hasAttribute('disabled') === input.disabled
    expect(applied).toBe(expected.ambientMetadataApplied)
    expect(control().getAttribute('aria-describedby')).toBe(expected.describedBy)
    ambient.unmount()
    render(field(<Switch disabled={!input.disabled} required={!input.required}/>))
    expect(control().hasAttribute('aria-required') === !input.required && control().hasAttribute('disabled') === !input.disabled).toBe(expected.localPrecedence)
  },
  'switch.validation': ({ input, expected }) => {
    const view = render(<Switch accessibleName="Alerts" error={input.error} required={input.required}/>)
    expect(control().getAttribute('aria-invalid')).toBe(expected.ariaInvalid)
    expect(control().getAttribute('aria-required')).toBe(expected.ariaRequired)
    // The non-color signal: a visible "!" indicator beside the double-bordered control.
    expect(view.container.querySelector('.hl-switch__error-indicator')?.textContent === '!' && control().classList.contains('hl-switch__control--invalid')).toBe(expected.nonColorSignal)
  },
  'switch.form-value': ({ input, expected }) => {
    const states = input.states as boolean[]
    const view = render(<Switch accessibleName="Alerts" checked={states[0]} name={input.name}/>)
    const values = states.map(checked => {
      view.rerender(<Switch accessibleName="Alerts" checked={checked} name={input.name}/>)
      const fields = document.querySelectorAll<HTMLInputElement>(`input[name="${input.name}"]`)
      expect(fields.length).toBe(1)
      return fields[0]!.value
    })
    expect(values).toEqual(expected.values)
  },
  'switch.sizes': ({ input, expected }) => {
    const rendered = (input.values as string[]).map(size => {
      const view = render(<Switch accessibleName="Alerts" checked size={size as never}/>)
      const result = { size: control().parentElement!.getAttribute('data-hl-size'), semantics: [control().getAttribute('role'), control().getAttribute('aria-checked'), control().getAttribute('aria-label'), control().getAttribute('type')].join('|') }
      view.unmount()
      return result
    })
    expect(rendered.map(value => value.size)).toEqual(expected.normalized)
    expect(rendered.every(value => value.semantics === rendered[0]!.semantics)).toBe(expected.semanticsEqual)
  },
  'switch.touch-target': ({ input, expected }) => {
    const tokens = (coarse: boolean) => {
      setMatchMedia(((query: string) => ({ ...defaultMatchMedia(query), matches: coarse && query === '(any-pointer: coarse)' })) as typeof window.matchMedia)
      const view = render(<Switch accessibleName="Alerts" size={input.size}/>)
      const root = control().parentElement!
      const result = { touch: root.getAttribute('data-hl-touch'), size: root.getAttribute('data-hl-size') }
      view.unmount()
      return result
    }
    const fine = tokens(false)
    const coarse = tokens(input.coarsePointer)
    // The marker switches on the invisible ::before target; the stylesheet sizes it to the fixture's minimum.
    const stylesheet = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    const rule = stylesheet.match(/\.hl-switch\[data-hl-touch='true'\] \.hl-switch__control::before\{min-inline-size:(\d+)px;min-block-size:(\d+)px\}/)
    expect(fine.touch).toBeNull()
    expect(coarse.touch).toBe('true')
    expect([Number(rule?.[1]), Number(rule?.[2])]).toEqual([expected.minimumTargetCssPixels, expected.minimumTargetCssPixels])
    expect(coarse.size === fine.size && coarse.size === input.size).toBe(expected.visiblePillUnchanged)
  },
  'switch.rtl': ({ input, expected }) => {
    const markers = (locale: string) => {
      const view = render(<HarborlineLocaleProvider locale={locale}><Switch accessibleName="Alerts" checked={input.checked}/></HarborlineLocaleProvider>)
      const root = control().parentElement!
      const result = { dir: root.getAttribute('dir'), checked: control().dataset.hlChecked, thumb: root.querySelector('.hl-switch__thumb') !== null, classes: control().className }
      view.unmount()
      return result
    }
    const ltr = markers('en-US')
    const rtl = markers(input.direction === 'rtl' ? 'ar-SA' : 'en-US')
    // Logical inset-inline-start in the stylesheet puts the checked thumb at the end of the direction.
    expect(rtl.dir === input.direction && rtl.checked === String(input.checked) && rtl.thumb).toBe(expected.thumbAtLogicalEnd)
    expect(rtl.classes === ltr.classes && ltr.dir !== rtl.dir).toBe(expected.borderStable)
  },
  'switch.state-copy-catalog': ({ input, expected }) => {
    const host = (child: React.ReactElement) => <HarborlineLocaleProvider locale={input.hostCatalog.locale} catalog={input.hostCatalog.entries}>{child}</HarborlineLocaleProvider>
    const stateCopy = (wrap: (child: React.ReactElement) => React.ReactElement, text: Record<string, string> = {}) =>
      (input.states as boolean[]).map(checked => {
        const view = render(wrap(<Switch accessibleName="Alerts" checked={checked} {...text}/>))
        const copy = view.container.querySelector('.hl-switch__state')!.textContent
        view.unmount()
        return copy
      })
    expect(stateCopy(child => child)).toEqual(expected.defaultCatalog)
    expect(stateCopy(host)).toEqual(expected.hostCatalog)
    expect(stateCopy(host, input.callerText)).toEqual(expected.callerText)
  },
  'switch.label-precedence': ({ input, expected }) => {
    render(<Switch accessibleName={input.accessibleName} checked={false} label={input.label}/>)
    expect(screen.getByRole('switch', { name: expected.accessibleName })).toBe(control())
  },
}
