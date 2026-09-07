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
