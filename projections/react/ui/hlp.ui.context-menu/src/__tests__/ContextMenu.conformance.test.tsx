import {fireEvent, render, screen} from '@testing-library/react'
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
  if (id === 'context-menu.keyboard-open' || id === 'context-menu.focus-return') { target.focus(); fireEvent.keyDown(target,{key:'F10',shiftKey:true}) } else fireEvent.contextMenu(target,{clientX:40,clientY:60})
  const menu = screen.getByRole('menu')
  if (id === 'context-menu.pointer-open') { expect(menu).toHaveStyle({left:'40px',top:'60px'}); return }
  if (id === 'context-menu.groups') { expect(screen.getAllByRole('menuitem')).toHaveLength(3); expect(screen.getByRole('separator')).toBeTruthy(); return }
  if (id === 'context-menu.pointer-selection') { fireEvent.click(screen.getByRole('menuitem',{name:'Copy'})); expect(copy).toHaveBeenCalledTimes(1); expect(screen.queryByRole('menu')).toBeNull(); return }
  if (id === 'context-menu.disabled-selection') { fireEvent.click(screen.getByRole('menuitem',{name:'Blocked'})); expect(blocked).not.toHaveBeenCalled(); expect(screen.getByRole('menu')).toBeTruthy(); return }
  if (id === 'context-menu.dismissal') { fireEvent.keyDown(menu,{key:'Escape'}); expect(screen.queryByRole('menu')).toBeNull(); return }
  if (id === 'context-menu.keyboard-open') { expect(menu).toBeTruthy(); return }
  if (id === 'context-menu.focus-entry') { expect(menu).toHaveAttribute('data-active-id','copy'); return }
  if (id === 'context-menu.keyboard-navigation') { fireEvent.keyDown(menu,{key:'End'}); expect(menu).toHaveAttribute('data-active-id','delete'); fireEvent.keyDown(menu,{key:'ArrowDown'}); expect(menu).toHaveAttribute('data-active-id','copy'); return }
  if (id === 'context-menu.keyboard-activation') { fireEvent.keyDown(menu,{key:'Enter'}); expect(copy).toHaveBeenCalledTimes(1); return }
  if (id === 'context-menu.focus-return') { fireEvent.keyDown(menu,{key:'Escape'}); await Promise.resolve(); expect(target).toHaveFocus(); return }
  if (id === 'context-menu.accessible-context') { expect(menu).toHaveAccessibleName('Actions'); expect(menu).toHaveAttribute('lang','ar-SA'); expect(menu).toHaveAttribute('dir','rtl'); return }
  if (id === 'context-menu.danger-icon') { const item=screen.getByRole('menuitem',{name:'Delete'}); expect(item).toHaveAttribute('data-danger','true'); expect(item.querySelector('[aria-hidden=true]')).toBeTruthy(); return }
  fireEvent.keyDown(menu,{key:'End'}); fireEvent.keyDown(menu,{key:'Escape'}); fireEvent.contextMenu(target); expect(screen.getByRole('menu')).toHaveAttribute('data-active-id','copy')
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
