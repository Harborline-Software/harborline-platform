import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Dialog } from '../Dialog'
import { fixture, sharedCases } from './fixtures'

describe('Dialog shared fixtures', () => {
  it('consumes every frozen shared case', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'dialog.closed-open-controlled',
      'dialog.title-description',
      'dialog.body-footer',
      'dialog.close-control',
      'dialog.escape-dismissal',
      'dialog.overlay-dismissal',
      'dialog.independent-dismissal',
      'dialog.focus-entry-cycle-restore',
      'dialog.modal-semantics',
      'dialog.scrollable-body',
      'dialog.localized-close-rtl',
      'dialog.long-content-reflow',
      'dialog.nested-content',
      'dialog.replacement',
      'dialog.projection-equivalence',
    ])
  })

  it('is caller-controlled, unmounted while closed, and exposes title, description, body, and optional footer relationships', () => {
    fixture(sharedCases, 'dialog.closed-open-controlled')
    fixture(sharedCases, 'dialog.title-description')
    fixture(sharedCases, 'dialog.body-footer')
    fixture(sharedCases, 'dialog.modal-semantics')
    const onOpenChange = vi.fn()
    const { rerender } = render(<Dialog open={false} onOpenChange={onOpenChange} title="Edit asset"><p>Body</p></Dialog>)
    expect(screen.queryByRole('dialog')).toBeNull()

    rerender(
      <Dialog
        description="Change the current asset."
        footer={<button type="button">Save</button>}
        onOpenChange={onOpenChange}
        open
        title="Edit asset"
      >
        <button type="button">Body action</button>
      </Dialog>,
    )
    const dialog = screen.getByRole('dialog', { name: 'Edit asset' })
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(dialog).toHaveAccessibleDescription('Change the current asset.')
    expect(screen.getByRole('button', { name: 'Body action' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
    expect(onOpenChange).not.toHaveBeenCalled()
  })

  it('requests close exactly once for the localized control, Escape, and overlay while preserving independent policies', async () => {
    fixture(sharedCases, 'dialog.close-control')
    fixture(sharedCases, 'dialog.escape-dismissal')
    fixture(sharedCases, 'dialog.overlay-dismissal')
    fixture(sharedCases, 'dialog.independent-dismissal')
    const onOpenChange = vi.fn()
    const { rerender } = render(
      <HarborlineLocaleProvider catalog={{ 'common.close': 'Fermer' }} locale="fr-FR">
        <Dialog onOpenChange={onOpenChange} open title="Policy"><span /></Dialog>
      </HarborlineLocaleProvider>,
    )
    await userEvent.setup().click(screen.getByRole('button', { name: 'Fermer' }))
    expect(onOpenChange).toHaveBeenLastCalledWith(false)
    expect(onOpenChange).toHaveBeenCalledTimes(1)

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onOpenChange).toHaveBeenCalledTimes(2)
    fireEvent.pointerDown(screen.getByTestId('dialog-overlay'))
    expect(onOpenChange).toHaveBeenCalledTimes(3)

    rerender(
      <Dialog closeIcon={false} closeOnEscape closeOnOverlayClick={false} onOpenChange={onOpenChange} open title="Independent">
        <button type="button">Inside</button>
      </Dialog>,
    )
    expect(screen.queryByRole('button', { name: 'Close' })).toBeNull()
    fireEvent.pointerDown(screen.getByTestId('dialog-overlay'))
    fireEvent.pointerDown(screen.getByRole('button', { name: 'Inside' }))
    expect(onOpenChange).toHaveBeenCalledTimes(3)
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onOpenChange).toHaveBeenCalledTimes(4)

    rerender(<Dialog closeOnEscape={false} onOpenChange={onOpenChange} open title="No escape"><span /></Dialog>)
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onOpenChange).toHaveBeenCalledTimes(4)
  })

  it('enters, cycles, contains, and restores focus while shielding background content', async () => {
    fixture(sharedCases, 'dialog.focus-entry-cycle-restore')
    fixture(sharedCases, 'dialog.nested-content')
    function Harness() {
      const [open, setOpen] = useState(false)
      return (
        <>
          <button onClick={() => setOpen(true)} type="button">Open</button>
          <Dialog
            footer={<button onClick={() => setOpen(false)} type="button">Save</button>}
            onOpenChange={setOpen}
            open={open}
            title="Focus"
          >
            <button type="button">Body action</button>
          </Dialog>
        </>
      )
    }
    render(<Harness />)
    const opener = screen.getByRole('button', { name: 'Open' })
    await userEvent.setup().click(opener)
    const close = screen.getByRole('button', { name: 'Close' })
    const body = document.querySelector<HTMLElement>('[data-dialog-body]')!
    const action = screen.getByRole('button', { name: 'Body action' })
    const save = screen.getByRole('button', { name: 'Save' })
    expect(close).toHaveFocus()
    expect(opener.parentElement).toHaveProperty('inert', true)
    await userEvent.setup().tab()
    expect(body).toHaveFocus()
    await userEvent.setup().tab()
    expect(action).toHaveFocus()
    await userEvent.setup().tab()
    expect(save).toHaveFocus()
    await userEvent.setup().tab()
    expect(close).toHaveFocus()
    await userEvent.setup().tab({ shift: true })
    expect(save).toHaveFocus()
    await userEvent.setup().click(save)
    expect(opener).toHaveFocus()
  })

  it('keeps only the body keyboard-scrollable and provides a polite scroll status', async () => {
    fixture(sharedCases, 'dialog.scrollable-body')
    vi.useFakeTimers()
    render(<Dialog onOpenChange={() => undefined} open title="Scrollable"><p>Long body</p></Dialog>)
    const body = document.querySelector<HTMLElement>('[data-dialog-body]')!
    Object.defineProperties(body, {
      clientHeight: { configurable: true, value: 100 },
      scrollHeight: { configurable: true, value: 400 },
    })
    fireEvent.scroll(body)
    fireEvent.keyDown(body, { key: 'End' })
    expect(body.scrollTop).toBe(300)
    expect(body).toHaveAttribute('tabindex', '0')
    await act(async () => { vi.runAllTimers() })
    expect(document.querySelector('.hl-dialog__status')).toHaveAttribute('aria-live', 'polite')
    expect(document.querySelector('.hl-dialog__header')).not.toHaveClass('hl-dialog__body')
    expect(document.querySelector('.hl-dialog__footer')).toBeNull()
    vi.useRealTimers()
  })

  it('uses locale direction, supports long content, and completely replaces controlled content and policy', () => {
    fixture(sharedCases, 'dialog.localized-close-rtl')
    fixture(sharedCases, 'dialog.long-content-reflow')
    fixture(sharedCases, 'dialog.replacement')
    fixture(sharedCases, 'dialog.projection-equivalence')
    const onOpenChange = vi.fn()
    const { rerender } = render(
      <HarborlineLocaleProvider catalog={{ 'common.close': 'إغلاق' }} direction="rtl" locale="ar-SA">
        <Dialog description="وصف طويل" onOpenChange={onOpenChange} open title="عنوان طويل"><p>المحتوى</p></Dialog>
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('dialog', { name: 'عنوان طويل' })).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('button', { name: 'إغلاق' })).toBeInTheDocument()
    for (let index = 0; index < 96; index += 1) {
      rerender(<Dialog closeOnEscape={index % 2 === 0} onOpenChange={onOpenChange} open title={`Title ${index}`}><p>{`Body ${index}`}</p></Dialog>)
    }
    expect(screen.getByRole('dialog', { name: 'Title 95' })).toBeInTheDocument()
    expect(screen.getByText('Body 95')).toBeInTheDocument()
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onOpenChange).not.toHaveBeenCalled()
  })
})
