import '@testing-library/jest-dom/vitest'
import * as React from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { Dialog } from '../../../hlp.ui.dialog/src/Dialog'
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

// b31 final check: a caller's own key handler on the trigger runs first and can take Tab; the popover's
// entry runs only when the caller left the key alone, and follows a trigger re-rendered while open.
describe('Popover focus order: a caller\'s trigger handler runs first (b31 final check)', () => {
  const takeTab = (event: React.KeyboardEvent) => {
    if (event.key !== 'Tab' || event.defaultPrevented) return
    event.preventDefault()
    named('Custom').focus()
  }

  it('a caller onKeyDown on the trigger that takes Tab wins', async () => {
    render(<><Popover defaultOpen><PopoverTrigger onKeyDown={takeTab}>Open</PopoverTrigger><PopoverContent aria-label="Details"><button type="button">Apply</button></PopoverContent></Popover><button type="button">Custom</button></>)
    await settle()
    named('Open').focus()
    expect(press(named('Open'))).toEqual(['handled', 'Custom'])
  })

  it('an asChild trigger\'s own onKeyDown that takes Tab wins', async () => {
    render(<><Popover defaultOpen><PopoverTrigger asChild><button type="button" onKeyDown={takeTab}>Open</button></PopoverTrigger><PopoverContent aria-label="Details"><button type="button">Apply</button></PopoverContent></Popover><button type="button">Custom</button></>)
    await settle()
    named('Open').focus()
    expect(press(named('Open'))).toEqual(['handled', 'Custom'])
  })

  it('a caller onKeyDown that leaves Tab alone keeps the popover\'s entry', async () => {
    const seen: string[] = []
    render(<Popover defaultOpen><PopoverTrigger onKeyDown={event => seen.push(event.key)}>Open</PopoverTrigger><PopoverContent aria-label="Details"><button type="button">Apply</button></PopoverContent></Popover>)
    await settle()
    named('Open').focus()
    expect([...press(named('Open')), seen.join()]).toEqual(['handled', 'Apply', 'Tab'])
  })

  it('a trigger re-rendered as a new element while open still enters the content', async () => {
    function Swap() {
      const [generation, setGeneration] = React.useState(0)
      return <><button type="button" onClick={() => setGeneration(value => value + 1)}>Swap</button>
        <Popover defaultOpen><PopoverTrigger key={generation}>Open</PopoverTrigger><PopoverContent aria-label="Details"><button type="button">Apply</button></PopoverContent></Popover></>
    }
    render(<Swap/>)
    await settle()
    const before = named('Open')
    fireEvent.click(named('Swap'))
    await settle()
    expect(named('Open')).not.toBe(before)
    named('Open').focus()
    expect(press(named('Open'))).toEqual(['handled', 'Apply'])
  })
})

// b31 final check, nested: the way in at depth. Tab on an inner trigger enters the inner content (not
// routed out of the outer content, where the inner trigger is the last stop), and Shift+Tab from the stop
// after the outer trigger walks into the inner content when the outer content's last stop is an open
// inner trigger (WHATWG HTML 6.6.2: the inner scope follows its trigger inside the outer scope).
describe('Popover focus order: the way in, nested (b31 final check)', () => {
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

  it('Tab on an open inner trigger that is the outer content\'s last stop enters the inner content', async () => {
    render(<Nested/>)
    await settle()
    named('Open B').focus()
    expect(press(named('Open B'))).toEqual(['handled', 'Y'])
  })

  it('Shift+Tab from the stop after the outer trigger walks into the open inner content', async () => {
    render(<Nested/>)
    await settle()
    named('After').focus()
    expect(press(named('After'), true)).toEqual(['handled', 'Y'])
  })
})

// Oracle: APG modal dialog keyboard interaction wraps Shift+Tab from first to last;
// an inert background popover must not consume the modal's key.
describe('Popover entry suspended by a modal', () => {
  it('does not enter content that became inert while open', async () => {
    render(<Page><button>Apply</button></Page>)
    await settle()
    ;(named('Details') as HTMLElement & { inert: boolean }).inert = true
    named('After').focus()
    expect(press(named('After'), true)).toEqual(['native', 'After'])
  })

  it('leaves reverse Tab to a Dialog opened from its content', async () => {
    function Example() {
      const [modal, setModal] = React.useState(false)
      return <>
        <Popover open><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details">
          <button onClick={() => setModal(true)}>Launch dialog</button>
        </PopoverContent></Popover>
        <Dialog open={modal} onOpenChange={setModal} title="Modal" closeIcon={false}>
          <button>First modal</button><button>Last modal</button>
        </Dialog>
      </>
    }
    render(<Example/>)
    await settle()
    fireEvent.click(named('Launch dialog'))
    await settle()
    const firstStop = document.querySelector<HTMLElement>('.hl-dialog__body')!
    firstStop.focus()
    expect(press(firstStop, true)).toEqual(['handled', 'Last modal'])
  })

  it.each([false, true])('ignores another modal scope (inert background: %s)', async inert => {
    render(<>
      <Popover open><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details"><button>Apply</button></PopoverContent></Popover>
      <section aria-modal="true" role="dialog" aria-label="Other modal"><button>Modal control</button></section>
    </>)
    await settle()
    if (inert) (named('Details') as HTMLElement & { inert: boolean }).inert = true
    named('Modal control').focus()
    expect(press(named('Modal control'), true)).toEqual(['native', 'Modal control'])
  })
})

// Oracle: WHATWG HTML tabindex-ordered focus navigation scope: positive values
// precede zero, ascending, with equal values in tree order.
describe('Popover positive tabindex order', () => {
  it.each([
    ['B', false, 'native', 'B'],
    ['A', true, 'native', 'A'],
    ['A', false, 'handled', 'After'],
    ['B', true, 'handled', 'Open'],
    ['Open', false, 'handled', 'B'],
    ['After', true, 'handled', 'A'],
  ] as const)('%s shift=%s follows sequential boundaries', async (from, shift, kind, to) => {
    render(<Page><button tabIndex={2}>A</button><button tabIndex={1}>B</button></Page>)
    await settle()
    named(from).focus()
    expect(press(named(from), shift)).toEqual([kind, to])
  })
})

// HTML focus rules: an editing host without tabindex participates at the implicit
// zero rank; explicit negative tabindex, hidden and inert hosts remain excluded.
describe('Popover editing-host Tab stops', () => {
  it.each(['', 'true', 'plaintext-only'])('keeps implicit %s editing hosts in forward and reverse-entry order', async value => {
    render(<Page><button>Apply</button><div contentEditable={value as 'true'} aria-label="Editor" suppressContentEditableWarning>Editable</div></Page>)
    await settle()
    named('Apply').focus()
    expect(press(named('Apply'))).toEqual(['native', 'Apply'])
    named('After').focus()
    expect(press(named('After'), true)).toEqual(['handled', 'Editor'])
  })

  it.each(['negative', 'hidden', 'inert'])('excludes an editing host when %s', async exclusion => {
    render(<Page><button>Apply</button><div contentEditable tabIndex={exclusion === 'negative' ? -1 : undefined} hidden={exclusion === 'hidden'} ref={node => { if (node && exclusion === 'inert') node.inert = true }} aria-label="Editor" suppressContentEditableWarning>Editable</div></Page>)
    await settle()
    named('Apply').focus()
    expect(press(named('Apply'))).toEqual(['handled', 'After'])
    expect(press(named('After'), true)).toEqual(['handled', 'Apply'])
  })
})
