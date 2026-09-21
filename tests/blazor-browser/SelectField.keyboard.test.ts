import { afterEach, expect, it, vi } from 'vitest'
// Exercise the shipped Blazor browser adapter without pretending bUnit executes browser defaults.
import { bind, focusTarget } from '../../projections/blazor/ui/hlp.ui.select-field/wwwroot/select-field.js'

afterEach(() => { document.body.replaceChildren() })

it.each(['combobox', 'searchbox'])('Blazor %s preserves native editing and IME, suppresses implicit submit, and disposes listeners', role => {
  document.body.innerHTML = '<form><span data-hl-open="true"><button type="button">Members</button><input role="' + role + '"><div role="listbox" tabindex="-1"></div></span><button type="button">Outside</button></form>'
  const root = document.querySelector('span')!
  const input = root.querySelector('input')!
  const callback = { invokeMethodAsync: vi.fn() }
  const binding = bind(root, callback)
  const key = (key: string, isComposing = false) => {
    const event = new KeyboardEvent('keydown', { key, isComposing, bubbles: true, cancelable: true })
    input.dispatchEvent(event)
    return event
  }
  for (const keyName of [' ', 'Home', 'End', 'ArrowLeft', 'ArrowRight', 'Backspace', 'Delete', 'Tab']) expect(key(keyName).defaultPrevented).toBe(false)
  expect(key('Enter').defaultPrevented).toBe(true)
  expect(key('ArrowDown').defaultPrevented).toBe(true)
  const parentKeys = vi.fn()
  document.addEventListener('keydown', parentKeys)
  input.dispatchEvent(new Event('compositionstart', { bubbles: true }))
  expect(key('Enter', true).defaultPrevented).toBe(true)
  expect(parentKeys).not.toHaveBeenCalled()
  input.dispatchEvent(new Event('compositionend', { bubbles: true }))
  key('ArrowDown')
  expect(parentKeys).toHaveBeenCalledOnce()
  document.removeEventListener('keydown', parentKeys)
  focusTarget(root, 'listbox')
  expect(root.querySelector('[role=listbox]')).toHaveFocus()
  const outside = document.querySelector('form > button')!
  outside.dispatchEvent(new Event('pointerdown', { bubbles: true }))
  expect(callback.invokeMethodAsync).toHaveBeenCalledWith('OnOutsidePointer')
  binding.dispose()
  expect(key('Enter').defaultPrevented).toBe(false)
  const count = callback.invokeMethodAsync.mock.calls.length
  outside.dispatchEvent(new Event('pointerdown', { bubbles: true }))
  expect(callback.invokeMethodAsync).toHaveBeenCalledTimes(count)
})

it('Blazor listbox prevents native Space scrolling and trigger Enter click duplication without trapping Tab', () => {
  document.body.innerHTML = '<span><button type="button">Members</button><div role="listbox" tabindex="-1"></div></span>'
  const root = document.querySelector('span')!
  const binding = bind(root, { invokeMethodAsync: vi.fn() })
  for (const target of [root.querySelector('button')!, root.querySelector('[role=listbox]')!]) {
    for (const key of [' ', 'Enter', 'ArrowDown', 'ArrowUp', 'Home', 'End']) {
      expect(target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))).toBe(false)
    }
    expect(target.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }))).toBe(true)
  }
  binding.dispose()
})
