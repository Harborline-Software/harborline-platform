import {act, fireEvent, render, screen} from '@testing-library/react'
import {expect, it, vi} from 'vitest'
import {ContextMenu, clampContextMenuPosition, type ContextMenuGroup} from '../ContextMenu'

const caseId = (JSON.parse(process.env.HARBORLINE_CONFORMANCE_FIXTURE ?? 'null') as {id?: string} | null)?.id
const allCases = [
  'context-menu.closed','context-menu.pointer-open','context-menu.groups','context-menu.pointer-selection','context-menu.disabled-selection',
  'context-menu.dismissal','context-menu.keyboard-open','context-menu.focus-entry','context-menu.keyboard-navigation','context-menu.keyboard-activation',
  'context-menu.focus-return','context-menu.accessible-context','context-menu.viewport-position','context-menu.danger-icon','context-menu.reopen-reset',
]

it.each(allCases)('%s', async id => {
  if (caseId && caseId !== id) return
  const copy = vi.fn(); const blocked = vi.fn(); const remove = vi.fn()
  const groups: ContextMenuGroup[] = [{items:[{id:'blocked',label:'Blocked',disabled:true,onSelect:blocked},{id:'copy',label:'Copy',onSelect:copy}]},{items:[{id:'delete',label:'Delete',danger:true,icon:<span>!</span>,onSelect:remove}]}]
  const {container} = render(<ContextMenu groups={groups} accessibleLabel="Actions" locale="ar-SA" direction="rtl"><button>Target</button></ContextMenu>)
  const target = screen.getByRole('button',{name:'Target'})
  if (id === 'context-menu.closed') { expect(screen.queryByRole('menu')).toBeNull(); return }
  if (id === 'context-menu.viewport-position') { expect(clampContextMenuPosition({x:995,y:795},{width:180,height:240},{width:1000,height:800})).toEqual({x:812,y:552}); return }
  if (id === 'context-menu.keyboard-open' || id === 'context-menu.focus-return' || id === 'context-menu.dismissal') { target.focus(); fireEvent.keyDown(target,{key:'F10',shiftKey:true}) } else fireEvent.contextMenu(target,{clientX:40,clientY:60})
  const menu = screen.getByRole('menu')
  if (id === 'context-menu.pointer-open') { expect(menu).toHaveStyle({left:'40px',top:'60px'}); return }
  if (id === 'context-menu.groups') { expect(screen.getAllByRole('menuitem')).toHaveLength(3); expect(screen.getByRole('separator')).toBeTruthy(); return }
  if (id === 'context-menu.pointer-selection') { fireEvent.click(screen.getByRole('menuitem',{name:'Copy'})); expect(copy).toHaveBeenCalledTimes(1); expect(screen.queryByRole('menu')).toBeNull(); return }
  if (id === 'context-menu.disabled-selection') {
    // APG: a disabled menuitem is aria-disabled, stays focusable, and cannot be activated by click, Enter or Space.
    const item = screen.getByRole('menuitem',{name:'Blocked'})
    expect(item).toHaveAttribute('aria-disabled','true'); expect(item).not.toHaveAttribute('disabled')
    expect(screen.getByRole('menuitem',{name:'Copy'})).not.toHaveAttribute('aria-disabled')
    expect(menu).toHaveAttribute('data-active-id','blocked')
    fireEvent.click(item); fireEvent.keyDown(menu,{key:'Enter'}); fireEvent.keyDown(menu,{key:' '})
    expect(blocked).not.toHaveBeenCalled(); expect(copy).not.toHaveBeenCalled(); expect(screen.getByRole('menu')).toBe(menu); return
  }
  if (id === 'context-menu.dismissal') {
    fireEvent.keyDown(menu,{key:'Escape'}); expect(screen.queryByRole('menu')).toBeNull()
    await Promise.resolve(); expect(target).toHaveFocus()
    // APG: Tab and Shift+Tab close the menu and keep their default action; focus is back on the opener within
    // the same keydown, so the browser's own Tab moves on from it. No selection is made.
    for (const shiftKey of [false, true]) {
      fireEvent.keyDown(target,{key:'F10',shiftKey:true}); expect(screen.getByRole('menuitem',{name:'Blocked'})).toHaveFocus()
      expect(fireEvent.keyDown(screen.getByRole('menu'),{key:'Tab',shiftKey}), `Shift ${shiftKey}: default kept`).toBe(true)
      expect(target, `Shift ${shiftKey}: focus on the opener before any microtask`).toHaveFocus()
      expect(screen.queryByRole('menu')).toBeNull()
    }
    expect(blocked).not.toHaveBeenCalled(); expect(copy).not.toHaveBeenCalled(); expect(remove).not.toHaveBeenCalled(); return
  }
  if (id === 'context-menu.keyboard-open') { expect(menu).toBeTruthy(); return }
  // APG: opening focuses the first item even when it is disabled.
  if (id === 'context-menu.focus-entry') { expect(menu).toHaveAttribute('data-active-id','blocked'); expect(screen.getByRole('menuitem',{name:'Blocked'})).toHaveFocus(); return }
  if (id === 'context-menu.keyboard-navigation') {
    // Every item is a stop, the disabled one included, and both directions wrap.
    const visited = ['ArrowDown','End','ArrowDown','ArrowUp','Home','ArrowDown'].map(key => { fireEvent.keyDown(menu,{key}); return menu.getAttribute('data-active-id') })
    expect(visited).toEqual(['copy','delete','blocked','delete','blocked','copy'])
    expect(screen.getByRole('menuitem',{name:'Copy'})).toHaveFocus(); return
  }
  if (id === 'context-menu.keyboard-activation') { fireEvent.keyDown(menu,{key:'ArrowDown'}); fireEvent.keyDown(menu,{key:'Enter'}); expect(copy).toHaveBeenCalledTimes(1); expect(screen.queryByRole('menu')).toBeNull(); return }
  if (id === 'context-menu.focus-return') { fireEvent.keyDown(menu,{key:'Escape'}); await Promise.resolve(); expect(target).toHaveFocus(); return }
  if (id === 'context-menu.accessible-context') { expect(menu).toHaveAccessibleName('Actions'); expect(menu).toHaveAttribute('lang','ar-SA'); expect(menu).toHaveAttribute('dir','rtl'); return }
  if (id === 'context-menu.danger-icon') { const item=screen.getByRole('menuitem',{name:'Delete'}); expect(item).toHaveAttribute('data-danger','true'); expect(item.querySelector('[aria-hidden=true]')).toBeTruthy(); return }
  fireEvent.keyDown(menu,{key:'End'}); fireEvent.keyDown(menu,{key:'Escape'}); fireEvent.contextMenu(target); expect(screen.getByRole('menu')).toHaveAttribute('data-active-id','blocked')
  expect(container).toBeTruthy()
})

// T-943 review 2: every close returns focus to the element that had it when the menu opened, after a
// pointer open as well as a keyboard one (APG menu pattern). The titles name no conformance case, so
// the per-case runner's `-t <case id>` still selects exactly one test.
const opens = ['pointer', 'keyboard'] as const
const closes = ['Escape', 'item selection', 'an outside press on nothing focusable'] as const
it.each(opens.flatMap(opening => closes.map(closing => [opening, closing] as const)))('a %s-opened menu returns focus to its opener after %s', async (opening, closing) => {
  if (caseId) return
  const groups: ContextMenuGroup[] = [{items: [{id: 'copy', label: 'Copy', onSelect: vi.fn()}]}]
  render(<><ContextMenu groups={groups} accessibleLabel="Actions"><button>Target</button></ContextMenu><p>Outside</p></>)
  const target = screen.getByRole('button', {name: 'Target'})
  // A browser focuses a focusable target on the press that precedes the context menu event.
  target.focus()
  if (opening === 'keyboard') fireEvent.keyDown(target, {key: 'F10', shiftKey: true})
  else fireEvent.contextMenu(target, {clientX: 40, clientY: 60})
  expect(screen.getByRole('menuitem', {name: 'Copy'})).toHaveFocus()
  if (closing === 'Escape') fireEvent.keyDown(screen.getByRole('menu'), {key: 'Escape'})
  else if (closing === 'item selection') fireEvent.click(screen.getByRole('menuitem', {name: 'Copy'}))
  else {
    fireEvent.mouseDown(screen.getByText('Outside'))
    // The browser's own order: microtasks queued by the listener run first, then the press's default
    // action moves focus off the menu onto the unfocusable target, which leaves it on <body>.
    await Promise.resolve()
    ;(document.activeElement as HTMLElement | null)?.blur()
  }
  await new Promise(done => setTimeout(done, 0))
  expect(screen.queryByRole('menu')).toBeNull()
  expect(target).toHaveFocus()
})

// T-943 review 3: a close gives focus back only if it is still where the menu left it. An item whose
// action moves focus (an inline editor here) keeps it there, after either kind of open, as React Aria's
// FocusScope restoreFocus does.
it.each(opens)('a %s-opened menu leaves focus where an item action moved it', async opening => {
  if (caseId) return
  const groups: ContextMenuGroup[] = [{items: [{id: 'rename', label: 'Rename', onSelect: () => screen.getByRole('textbox', {name: 'Editor'}).focus()}]}]
  render(<><ContextMenu groups={groups} accessibleLabel="Actions"><button>Target</button></ContextMenu><input aria-label="Editor" /></>)
  const target = screen.getByRole('button', {name: 'Target'})
  target.focus()
  if (opening === 'keyboard') fireEvent.keyDown(target, {key: 'F10', shiftKey: true})
  else fireEvent.contextMenu(target, {clientX: 40, clientY: 60})
  if (opening === 'keyboard') fireEvent.keyDown(screen.getByRole('menu'), {key: 'Enter'})
  else fireEvent.click(screen.getByRole('menuitem', {name: 'Rename'}))
  await new Promise(done => setTimeout(done, 0))
  expect(screen.queryByRole('menu')).toBeNull()
  expect(screen.getByRole('textbox', {name: 'Editor'})).toHaveFocus()
})

// PR 396 review: a disabled item is focusable, so a pointer press focuses it. It then becomes the active item, and
// Enter or Space acts on it (nothing) rather than on the item that was active before the press.
it('focusing a disabled item makes it active, so Enter and Space then select nothing', () => {
  if (caseId) return
  const remove = vi.fn(); const blocked = vi.fn()
  const groups: ContextMenuGroup[] = [{items: [{id: 'blocked', label: 'Blocked', disabled: true, onSelect: blocked}, {id: 'delete', label: 'Delete', onSelect: remove}]}]
  render(<ContextMenu groups={groups} accessibleLabel="Actions"><button>Target</button></ContextMenu>)
  fireEvent.contextMenu(screen.getByRole('button', {name: 'Target'}), {clientX: 40, clientY: 60})
  const menu = screen.getByRole('menu')
  fireEvent.keyDown(menu, {key: 'End'})
  expect(menu).toHaveAttribute('data-active-id', 'delete')
  const item = screen.getByRole('menuitem', {name: 'Blocked'})
  act(() => item.focus()); fireEvent.click(item) // a browser focuses the button on the press before the click
  expect(menu).toHaveAttribute('data-active-id', 'blocked')
  for (const key of ['Enter', ' ']) fireEvent.keyDown(item, {key})
  expect(remove).not.toHaveBeenCalled(); expect(blocked).not.toHaveBeenCalled()
  expect(screen.getByRole('menu')).toHaveAttribute('data-active-id', 'blocked')
  expect(item).toHaveAttribute('data-active', 'true')
})

it('a close whose opener left the document returns focus to the trigger instead', async () => {
  if (caseId) return
  const groups: ContextMenuGroup[] = [{items: [{id: 'copy', label: 'Copy', onSelect: vi.fn()}]}]
  render(<ContextMenu groups={groups} accessibleLabel="Actions"><button>Target</button></ContextMenu>)
  // An opener outside React's tree, so removing it mid-menu is the host's doing, not React's.
  const opener = document.body.appendChild(Object.assign(document.createElement('button'), {textContent: 'Opener'}))
  opener.focus()
  fireEvent.contextMenu(screen.getByRole('button', {name: 'Target'}), {clientX: 40, clientY: 60})
  opener.remove()
  fireEvent.keyDown(screen.getByRole('menu'), {key: 'Escape'})
  await new Promise(done => setTimeout(done, 0))
  expect(screen.getByRole('button', {name: 'Target'})).toHaveFocus()
})

// T-943 review 4: with nothing focused at open there is no opener, so no close moves focus. A menu
// opened on unfocusable content leaves focus on <body> after Escape, a selection or an outside press,
// instead of jumping (and scrolling) to the trigger's first focusable element.
it.each(closes)('a menu opened while nothing had focus leaves focus on <body> after %s', async closing => {
  if (caseId) return
  const groups: ContextMenuGroup[] = [{items: [{id: 'copy', label: 'Copy', onSelect: vi.fn()}]}]
  render(<><ContextMenu groups={groups} accessibleLabel="Actions"><div><span>Row text</span><button>Edit row 1</button></div></ContextMenu><p>Outside</p></>)
  ;(document.activeElement as HTMLElement | null)?.blur()
  expect(document.activeElement).toBe(document.body)
  fireEvent.contextMenu(screen.getByText('Row text'), {clientX: 40, clientY: 60})
  if (closing === 'Escape') fireEvent.keyDown(screen.getByRole('menu'), {key: 'Escape'})
  else if (closing === 'item selection') fireEvent.click(screen.getByRole('menuitem', {name: 'Copy'}))
  else {
    fireEvent.mouseDown(screen.getByText('Outside'))
    await Promise.resolve()
    ;(document.activeElement as HTMLElement | null)?.blur()
  }
  await new Promise(done => setTimeout(done, 0))
  expect(screen.queryByRole('menu')).toBeNull()
  expect(document.activeElement).toBe(document.body)
})
