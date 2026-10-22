import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Dialog } from '../Dialog'
import { qualityCases } from './fixtures'

describe('Dialog React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'dialog.quality.modal-name',
      'dialog.quality.reflow',
      'dialog.quality.catalog',
      'dialog.quality.pseudo',
      'dialog.quality.light-dark',
      'dialog.quality.tokens',
      'dialog.quality.forced-colors',
      'dialog.quality.visual-parity',
      'dialog.quality.keyboard',
      'dialog.quality.focus',
      'dialog.quality.dismissal',
      'dialog.quality.rtl',
      'dialog.quality.reduced-motion',
    ])
  })

  it('uses catalog copy and logical RTL layout without clipping long pseudo-localized content', () => {
    const long = '[!! A deliberately expanded pseudo-localized value that must wrap safely !!]'
    render(
      <HarborlineLocaleProvider catalog={{ 'common.close': `[!! Close expanded !!]` }} direction="rtl" locale="ar-SA">
        <Dialog description={long} footer={<button type="button">{long}</button>} onOpenChange={vi.fn()} open theme="light" title={long}>
          <p>{long}</p>
        </Dialog>
      </HarborlineLocaleProvider>,
    )
    const dialog = screen.getByRole('dialog', { name: long })
    expect(dialog).toHaveAttribute('dir', 'rtl')
    expect(dialog.closest('.hl-dialog__portal')).toHaveAttribute('data-theme', 'light')
    expect(screen.getByRole('button', { name: '[!! Close expanded !!]' })).toBeInTheDocument()
  })

  it('publishes semantic theme, reflow, forced-color, focus, and reduced-motion CSS', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-dialog-surface')
    expect(css).toContain('--hl-dialog-overlay')
    expect(css).toContain('--hl-dialog-focus')
    expect(css).toContain('inline-size: calc(100dvw - 2rem)')
    expect(css).toContain('max-block-size')
    expect(css).toContain('padding-inline')
    expect(css).toContain('.hl-dialog__footer button')
    expect(css).toContain("[data-theme='dark'] .hl-dialog__surface")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/animation:\s*none/)
    expect(css).toMatch(/transition:\s*none/)
  })

  // An inner widget (a menu, combobox or armed guard) owns its Escape first; the dialog dismisses
  // only on an Escape nothing inside it handled (WAI-ARIA APG dialog and menu patterns).
  it('lets a widget inside the dialog handle Escape before the dialog dismisses', () => {
    const onOpenChange = vi.fn()
    const inner = vi.fn()
    render(
      <Dialog onOpenChange={onOpenChange} open title="Edit">
        <input aria-label="Picker" onKeyDown={event => { if (event.key === 'Escape') { inner(); event.preventDefault() } }} />
      </Dialog>,
    )
    fireEvent.keyDown(screen.getByRole('textbox', { name: 'Picker' }), { key: 'Escape' })
    expect(inner).toHaveBeenCalledOnce()
    expect(onOpenChange).not.toHaveBeenCalled()
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(onOpenChange).toHaveBeenCalledExactlyOnceWith(false)
  })

  it('dismisses only the topmost dialog when a second dialog opens later', () => {
    const first = vi.fn()
    const second = vi.fn()
    const view = render(<><Dialog onOpenChange={first} open title="First"><p>First body</p></Dialog></>)
    view.rerender(
      <>
        <Dialog onOpenChange={first} open title="First"><p>First body</p></Dialog>
        <Dialog onOpenChange={second} open title="Second"><input aria-label="Second field" /></Dialog>
      </>,
    )
    fireEvent.keyDown(screen.getByRole('textbox', { name: 'Second field' }), { key: 'Escape' })
    expect(second).toHaveBeenCalledExactlyOnceWith(false)
    expect(first).not.toHaveBeenCalled()
  })
})
