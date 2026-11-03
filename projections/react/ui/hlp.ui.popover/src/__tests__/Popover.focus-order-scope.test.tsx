import '@testing-library/jest-dom/vitest'
import * as React from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { Popover, PopoverContent, PopoverTrigger } from '../Popover'

// T-1128, b31 review follow-up. The content's focus navigation scope sits inside its trigger's scope
// (WHATWG HTML 6.6.2), at any depth, and the content's own Tab stops are the browser's: a radio group is
// one stop (its checked radio, else its first), tabindex=-1 is not a stop, and the focused element is
// located by document position. Each key is dispatched and read: handled (the popover moved focus) or
// native (left to the browser), and where focus is.
const press = (element: Element, shiftKey = false) => {
  const native = fireEvent.keyDown(element, { key: 'Tab', shiftKey })
  const active = document.activeElement as HTMLElement | null
  const focus = !active || active === document.body ? 'BODY' : (active.getAttribute('aria-label') || active.textContent)
  return [native ? 'native' : 'handled', focus]
}
const named = (name: string) => [...document.querySelectorAll<HTMLElement>('button, input, h2, p')].find(node => (node.getAttribute('aria-label') || node.textContent) === name)!
async function settle() { await act(async () => { await Promise.resolve() }) }

function Single({ children, after = true, before = true }: { children: React.ReactNode, after?: boolean, before?: boolean }) {
  return <>
    {before ? <button type="button">Before</button> : null}
    <Popover defaultOpen><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details">{children}</PopoverContent></Popover>
    {after ? <button type="button">After</button> : null}
  </>
}

describe('Popover focus order: nested popovers (b31 review 1)', () => {
  function Nested() {
    return <>
      <button type="button">Before</button>
      <Popover defaultOpen><PopoverTrigger>Open A</PopoverTrigger><PopoverContent aria-label="A">
        <button type="button">X</button>
        <Popover defaultOpen><PopoverTrigger>Open B</PopoverTrigger><PopoverContent aria-label="B"><button type="button">Y</button></PopoverContent></Popover>
      </PopoverContent></Popover>
      <button type="button">After</button>
    </>
  }

  it('Tab from the inner content, its trigger last in the outer content, continues after the outer trigger', async () => {
    render(<Nested/>)
    await settle()
    named('Y').focus()
    expect(press(named('Y'))).toEqual(['handled', 'After'])
    expect([screen.getByRole('dialog', { name: 'A' }), screen.getByRole('dialog', { name: 'B' })]).toHaveLength(2)
  })

  it('Shift+Tab from the inner content goes to the inner trigger, and from the outer content to the outer trigger', async () => {
    render(<Nested/>)
    await settle()
    named('Y').focus()
    expect(press(named('Y'), true)).toEqual(['handled', 'Open B'])
    named('X').focus()
    expect(press(named('X'), true)).toEqual(['handled', 'Open A'])
  })
})

describe('Popover focus order: the content\'s own Tab stops (b31 review 2)', () => {
  it('Tab from a checked radio before the rest of its group, the group last, leaves the content', async () => {
    render(<Single><button type="button">Apply</button><input type="radio" name="g" aria-label="R1" defaultChecked/><input type="radio" name="g" aria-label="R2"/></Single>)
    await settle()
    named('R1').focus()
    expect(press(named('R1'))).toEqual(['handled', 'After'])
  })

  it('Shift+Tab from a checked radio after the rest of its group, the group first, leaves the content', async () => {
    render(<Single><input type="radio" name="g" aria-label="R1"/><input type="radio" name="g" aria-label="R2" defaultChecked/><button type="button">Apply</button></Single>)
    await settle()
    named('R2').focus()
    expect(press(named('R2'), true)).toEqual(['handled', 'Open'])
  })

  it('a radio group with nothing checked is one stop at its first radio: Tab from it to the next control stays native', async () => {
    render(<Single><input type="radio" name="g" aria-label="R1"/><input type="radio" name="g" aria-label="R2"/><button type="button">Apply</button></Single>)
    await settle()
    named('R1').focus()
    expect(press(named('R1'))[0]).toBe('native')
  })

  it('Tab from a focused tabindex=-1 note after the last control leaves the content', async () => {
    render(<Single><button type="button">Apply</button><p tabIndex={-1} aria-label="Note">note</p></Single>)
    await settle()
    named('Note').focus()
    expect(press(named('Note'))).toEqual(['handled', 'After'])
  })

  it('Shift+Tab from a focused tabindex=-1 heading before the first control leaves the content', async () => {
    render(<Single><h2 tabIndex={-1} aria-label="Heading">H</h2><button type="button">Apply</button></Single>)
    await settle()
    named('Heading').focus()
    expect(press(named('Heading'), true)).toEqual(['handled', 'Open'])
  })

  it('Tab from a tabindex=-1 element before a control stays native', async () => {
    render(<Single><p tabIndex={-1} aria-label="Note">note</p><button type="button">Apply</button></Single>)
    await settle()
    named('Note').focus()
    expect(press(named('Note'))[0]).toBe('native')
  })
})

describe('Popover focus order: guards (b31 review 3)', () => {
  it('skips elements after the trigger that are not Tab stops: disabled, tabindex=-1, hidden, inside inert', async () => {
    render(<>
      <button type="button">Before</button>
      <Popover defaultOpen><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details"><button type="button">Apply</button></PopoverContent></Popover>
      <button type="button" disabled>Off</button><button type="button" tabIndex={-1}>Minus</button><button type="button" hidden>Hidden</button>
      <div ref={node => { if (node) (node as HTMLElement & { inert: boolean }).inert = true }}><button type="button">Inert</button></div>
      <button type="button">After</button>
    </>)
    await settle()
    named('Apply').focus()
    expect(press(named('Apply'))).toEqual(['handled', 'After'])
  })

  it('a key a host onKeyDown already handled is left alone, even with a stop after the trigger', async () => {
    render(<>
      <Popover defaultOpen><PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details" onKeyDown={event => { if (event.key === 'Tab') event.preventDefault() }}><button type="button">Apply</button></PopoverContent></Popover>
      <button type="button">After</button>
    </>)
    await settle()
    named('Apply').focus()
    expect(press(named('Apply'))).toEqual(['handled', 'Apply'])
  })

  it('the content of another open popover after the trigger is not the trigger\'s next stop', async () => {
    render(<>
      <Popover defaultOpen><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details"><button type="button">Apply</button></PopoverContent></Popover>
      <Popover defaultOpen><PopoverTrigger>Other</PopoverTrigger><PopoverContent aria-label="Elsewhere"><button type="button">Far</button></PopoverContent></Popover>
    </>)
    await settle()
    named('Apply').focus()
    expect(press(named('Apply'))).toEqual(['handled', 'Other'])
    named('Far').focus()
    expect(press(named('Far'))).toEqual(['native', 'Far'])
  })
})

describe('Popover focus order: a trigger that cannot take focus (b31 review note 1)', () => {
  it('Shift+Tab out goes to the stop before a trigger that became disabled, rather than trapping', async () => {
    render(<Single><button type="button">Apply</button></Single>)
    await settle()
    const trigger = screen.getByRole('button', { name: 'Open' }) as HTMLButtonElement
    trigger.disabled = true
    named('Apply').focus()
    expect(press(named('Apply'), true)).toEqual(['handled', 'Before'])
  })

  it('Shift+Tab out goes to the stop before a trigger that became hidden', async () => {
    render(<Single><button type="button">Apply</button></Single>)
    await settle()
    screen.getByRole('button', { name: 'Open' }).hidden = true
    named('Apply').focus()
    expect(press(named('Apply'), true)).toEqual(['handled', 'Before'])
  })

  it('with no stop before an unfocusable trigger, Shift+Tab is left to the browser', async () => {
    render(<Single before={false}><button type="button">Apply</button></Single>)
    await settle()
    ;(screen.getByRole('button', { name: 'Open' }) as HTMLButtonElement).disabled = true
    named('Apply').focus()
    expect(press(named('Apply'), true)).toEqual(['native', 'Apply'])
  })
})
