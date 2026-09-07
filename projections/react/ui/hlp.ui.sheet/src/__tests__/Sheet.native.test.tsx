import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { Sheet, SheetContent, SheetTitle, SheetTrigger } from '../Sheet'
import { qualityCases } from './fixtures'

describe('Sheet React projection', () => {
  it('consumes every frozen quality case', () => {
    expect([...new Set(qualityCases.map(value => value.id))]).toEqual([
      'sheet.quality.dialog',
      'sheet.quality.keyboard',
      'sheet.quality.focus',
      'sheet.quality.dismissal',
      'sheet.quality.reflow',
      'sheet.quality.caller-copy',
      'sheet.quality.rtl',
      'sheet.quality.pseudo',
      'sheet.quality.light-dark',
      'sheet.quality.tokens',
      'sheet.quality.forced-colors',
      'sheet.quality.reduced-motion',
      'sheet.quality.visual-parity',
    ])
  })

  it('keeps an explicitly nonmodal sheet out of the modal focus/inert path', () => {
    render(
      <>
        <button type="button">Outside</button>
        <Sheet open modal={false}>
          <SheetContent closeLabel="Fermer">
            <SheetTitle>Calendrier</SheetTitle>
          </SheetContent>
        </Sheet>
      </>,
    )

    expect(screen.getByRole('dialog', { name: 'Calendrier' })).toHaveAttribute('aria-modal', 'false')
    expect(screen.queryByTestId('sheet-overlay')).toBeNull()
    expect(screen.getByRole('button', { name: 'Outside' })).not.toHaveAttribute('inert')
    expect(screen.getByRole('button', { name: 'Fermer' })).toBeInTheDocument()
  })

  it('uses only the caller supplied localized close label', () => {
    render(<Sheet open><SheetContent closeLabel="Schließen"><SheetTitle>Einstellungen</SheetTitle></SheetContent></Sheet>)
    expect(screen.getByRole('button', { name: 'Schließen' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Close' })).toBeNull()
  })

  it('keeps the requested physical side under RTL', () => {
    render(
      <div dir="rtl">
        <Sheet open><SheetContent closeLabel="إغلاق" side="left"><SheetTitle>التقويم</SheetTitle></SheetContent></Sheet>
      </div>,
    )
    expect(screen.getByRole('dialog', { name: 'التقويم' })).toHaveAttribute('data-side', 'left')
  })

  it('traps modal focus and allows nonmodal focus to leave', async () => {
    const { rerender } = render(
      <>
        <button type="button">Outside</button>
        <Sheet open><SheetContent closeLabel="Close"><SheetTitle>Modal</SheetTitle></SheetContent></Sheet>
      </>,
    )
    const modalClose = screen.getByRole('button', { name: 'Close' })
    expect(modalClose).toHaveFocus()
    await userEvent.setup().tab()
    expect(modalClose).toHaveFocus()

    rerender(
      <>
        <button type="button">Outside</button>
        <Sheet open modal={false}><SheetContent closeLabel="Close"><SheetTitle>Nonmodal</SheetTitle></SheetContent></Sheet>
      </>,
    )
    const outside = screen.getByRole('button', { name: 'Outside' })
    outside.focus()
    expect(outside).toHaveFocus()
  })

  it('preserves an asChild trigger without nesting controls', async () => {
    render(
      <Sheet>
        <SheetTrigger asChild><button data-consumer="trigger" type="button">Open</button></SheetTrigger>
        <SheetContent closeLabel="Close"><SheetTitle>Panel</SheetTitle></SheetContent>
      </Sheet>,
    )
    const trigger = screen.getByRole('button', { name: 'Open' })
    expect(trigger).toHaveAttribute('data-consumer', 'trigger')
    expect(trigger.querySelector('button')).toBeNull()
    await userEvent.setup().click(trigger)
    expect(screen.getByRole('dialog', { name: 'Panel' })).toBeInTheDocument()
  })

  it('publishes reflow, logical, token, forced-color, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-sheet-surface')
    expect(css).toContain('max-inline-size')
    expect(css).toContain('max-block-size')
    expect(css).toContain('text-align: start')
    expect(css).toContain("[data-theme='dark'] .hl-sheet__content")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/animation:\s*none/)
    expect(css).toMatch(/transition:\s*none/)
  })
})
