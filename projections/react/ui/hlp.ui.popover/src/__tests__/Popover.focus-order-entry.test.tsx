import '@testing-library/jest-dom/vitest'
import * as React from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { Popover, PopoverContent, PopoverTrigger } from '../Popover'

// T-1128, b31 re-review. The content's scope sits right after its trigger (WHATWG HTML 6.6.2), so the
// way in mirrors the way out: while open, Tab on the trigger goes to the content's first Tab stop (or the
// content element when it has none, as b30's focus fallback), and Shift+Tab from the next stop after the
// trigger in the trigger's own scope goes to the content's last stop. Closed, the trigger is untouched.
const press = (element: Element, shiftKey = false) => {
  const native = fireEvent.keyDown(element, { key: 'Tab', shiftKey })
  const active = document.activeElement as HTMLElement | null
  return [native ? 'native' : 'handled', !active || active === document.body ? 'BODY' : (active.getAttribute('aria-label') || active.textContent)]
}
const named = (name: string) => [...document.querySelectorAll<HTMLElement>('button, input, [role="dialog"]')].find(node => (node.getAttribute('aria-label') || node.textContent) === name)!
async function settle() { await act(async () => { await Promise.resolve() }) }

function Page({ children, open = true, afterProps = {} }: { children: React.ReactNode, open?: boolean, afterProps?: React.ButtonHTMLAttributes<HTMLButtonElement> }) {
  return <>
    <button type="button">Before</button>
    <Popover open={open}><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details">{children}</PopoverContent></Popover>
    <button type="button" {...afterProps}>After</button>
  </>
}

describe('Popover focus order: the way into the content (b31 re-review)', () => {
  it('Tab on the trigger while open goes to the content\'s first stop', async () => {
    render(<Page><button type="button">Apply</button><button type="button">Second</button></Page>)
    await settle()
    named('Open').focus()
    expect(press(named('Open'))).toEqual(['handled', 'Apply'])
  })

  it('with no stop in the content, Tab on the trigger goes to the content element', async () => {
    render(<Page><p>Read only</p></Page>)
    await settle()
    named('Open').focus()
    expect(press(named('Open'))).toEqual(['handled', 'Details'])
  })

  it('Shift+Tab out to the trigger, then Tab, re-enters the content', async () => {
    render(<Page><button type="button">Apply</button></Page>)
    await settle()
    named('Apply').focus()
    expect(press(named('Apply'), true)).toEqual(['handled', 'Open'])
    expect(press(named('Open'))).toEqual(['handled', 'Apply'])
  })

  it('Shift+Tab from the next stop after the trigger goes to the content\'s last stop', async () => {
    render(<Page><button type="button">Apply</button><button type="button">Last</button></Page>)
    await settle()
    named('After').focus()
    expect(press(named('After'), true)).toEqual(['handled', 'Last'])
  })

  it('Shift+Tab from a later stop than the next one stays native', async () => {
    render(<><Page><button type="button">Apply</button></Page><button type="button">Further</button></>)
    await settle()
    named('Further').focus()
    expect(press(named('Further'), true)).toEqual(['native', 'Further'])
  })

  it('a Shift+Tab the next stop already handled is left alone', async () => {
    render(<Page afterProps={{ onKeyDown: event => event.preventDefault() }}><button type="button">Apply</button></Page>)
    await settle()
    named('After').focus()
    expect(press(named('After'), true)).toEqual(['handled', 'After'])
  })

  it('closed, Tab on the trigger and Shift+Tab after it stay native', async () => {
    render(<Page open={false}><button type="button">Apply</button></Page>)
    await settle()
    named('Open').focus()
    expect(press(named('Open'))).toEqual(['native', 'Open'])
    named('After').focus()
    expect(press(named('After'), true)).toEqual(['native', 'After'])
  })
})

describe('Popover focus order: radio groups as one stop (b31 re-review test gap)', () => {
  it('the next stop after the trigger is a radio group\'s checked radio, not its first', async () => {
    render(<>
      <Popover defaultOpen><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details"><button type="button">Apply</button></PopoverContent></Popover>
      <input type="radio" name="page" aria-label="P1"/><input type="radio" name="page" aria-label="P2" defaultChecked/>
    </>)
    await settle()
    named('Apply').focus()
    expect(press(named('Apply'))).toEqual(['handled', 'P2'])
  })

  it('radios with one name in different forms are different groups: Tab between them stays native', async () => {
    render(<Page><form><input type="radio" name="g" aria-label="R1" defaultChecked/></form><form><input type="radio" name="g" aria-label="R2" defaultChecked/></form></Page>)
    await settle()
    named('R1').focus()
    expect(press(named('R1'))[0]).toBe('native')
  })
})
