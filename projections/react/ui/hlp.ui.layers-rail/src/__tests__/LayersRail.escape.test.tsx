import * as React from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Sheet, SheetContent, SheetTitle } from '../../../hlp.ui.sheet/src/Sheet'
import { Host } from './setup'

// Escape belongs to the innermost layer that uses it. The rail uses Escape only to clear an active
// lens; then it marks the key handled so an enclosing Dialog or Sheet (which leave a handled Escape
// alone) stays open. With no active lens the rail has no use for Escape and leaves it to them.
const lens = () => document.querySelector<HTMLButtonElement>('.hl-layers-rail__lens-activate')!

function InSheet({ initialActive }: { initialActive: string | null }) {
  const [open, setOpen] = React.useState(true)
  return <><span data-testid="sheet-open">{String(open)}</span>
    <Sheet open={open} onOpenChange={setOpen}><SheetContent closeLabel="Close panel"><SheetTitle>Panel</SheetTitle><Host initialActive={initialActive}/></SheetContent></Sheet></>
}

describe('LayersRail Escape layering', () => {
  it('leaves Escape unhandled when no lens is active', () => {
    render(<Host initialActive={null}/>)
    lens().focus()
    const notPrevented = fireEvent.keyDown(lens(), { key: 'Escape' })
    expect(notPrevented).toBe(true)
  })

  it('clears an active lens on Escape, marks the key handled and keeps focus', () => {
    render(<Host initialActive="layout"/>)
    lens().focus()
    const notPrevented = fireEvent.keyDown(lens(), { key: 'Escape' })
    expect([notPrevented, document.querySelector('.hl-layers-rail__status'), document.activeElement === lens()]).toEqual([false, null, true])
  })

  it('inside a real Sheet: the first Escape clears the lens, the next closes the Sheet', async () => {
    render(<InSheet initialActive="layout"/>)
    lens().focus()
    fireEvent.keyDown(lens(), { key: 'Escape' })
    await act(async () => { await Promise.resolve() })
    expect([document.querySelector('.hl-layers-rail__status'), screen.getByTestId('sheet-open').textContent, document.activeElement === lens()]).toEqual([null, 'true', true])
    fireEvent.keyDown(lens(), { key: 'Escape' })
    await act(async () => { await Promise.resolve() })
    expect(screen.getByTestId('sheet-open').textContent).toBe('false')
  })

  it('inside a real Sheet: with no active lens, one Escape closes the Sheet', async () => {
    render(<InSheet initialActive={null}/>)
    lens().focus()
    fireEvent.keyDown(lens(), { key: 'Escape' })
    await act(async () => { await Promise.resolve() })
    expect(screen.getByTestId('sheet-open').textContent).toBe('false')
  })
})
