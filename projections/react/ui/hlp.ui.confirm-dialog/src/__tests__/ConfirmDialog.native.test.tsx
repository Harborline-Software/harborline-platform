import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { ConfirmDialog } from '../ConfirmDialog'
import { fixtureCases, fixtureIds } from './fixtures'

const baseProps = {
  title: 'Delete the record?',
  description: 'This cannot be undone.',
}

function renderConfirm(
  overrides: Partial<React.ComponentProps<typeof ConfirmDialog>> = {},
  locale: {catalog?: Record<string, string>; direction?: 'ltr' | 'rtl'; locale?: string} = {},
) {
  const onConfirm = vi.fn()
  const onOpenChange = vi.fn()
  const result = render(
    <HarborlineLocaleProvider catalog={locale.catalog ?? {}} direction={locale.direction ?? 'ltr'} locale={locale.locale ?? 'en-US'}>
      <ConfirmDialog {...baseProps} onConfirm={onConfirm} onOpenChange={onOpenChange} open {...overrides} />
    </HarborlineLocaleProvider>,
  )
  return {onConfirm, onOpenChange, ...result}
}

describe('ConfirmDialog React projection', () => {
  it('consumes every frozen fixture case', () => {
    // Guards against a fixture being added without a corresponding assertion below. If this list
    // and the fixture file drift, the module claims coverage it does not have.
    expect(fixtureIds).toEqual([
      'confirm-dialog.controlled-open',
      'confirm-dialog.confirm-invokes-once-then-closes',
      'confirm-dialog.cancel-closes-without-confirm',
      'confirm-dialog.localized-labels',
      'confirm-dialog.label-override',
      'confirm-dialog.key-echo-fallback',
      'confirm-dialog.destructive-emphasis',
      'confirm-dialog.dismissal-passthrough',
      'confirm-dialog.composes-dialog',
      'confirm-dialog.long-content-reflow',
      'confirm-dialog.locale-rtl',
      'confirm-dialog.replacement',
      'confirm-dialog.action-classes',
      'confirm-dialog.projection-equivalence',
    ])
  })

  // confirm-dialog.controlled-open
  it('mounts nothing while closed and requests state changes rather than self-closing', async () => {
    const {onOpenChange, rerender} = renderConfirm({open: false})
    expect(screen.queryByRole('dialog')).toBeNull()

    rerender(
      <HarborlineLocaleProvider catalog={{}} direction="ltr" locale="en-US">
        <ConfirmDialog {...baseProps} onConfirm={vi.fn()} onOpenChange={onOpenChange} open />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('dialog')).toBeInTheDocument()

    // Controlled: pressing cancel must REQUEST closure, not unmount itself. The dialog stays
    // mounted because `open` is still true — the caller owns that state.
    await userEvent.click(screen.getByRole('button', {name: 'Cancel'}))
    expect(onOpenChange).toHaveBeenCalledWith(false)
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  // confirm-dialog.confirm-invokes-once-then-closes — ORDER IS LOAD-BEARING.
  it('invokes onConfirm exactly once and strictly before requesting close', async () => {
    const order: string[] = []
    const onConfirm = vi.fn(() => { order.push('confirm') })
    const onOpenChange = vi.fn(() => { order.push('close') })
    render(
      <HarborlineLocaleProvider catalog={{}} direction="ltr" locale="en-US">
        <ConfirmDialog {...baseProps} onConfirm={onConfirm} onOpenChange={onOpenChange} open />
      </HarborlineLocaleProvider>,
    )

    await userEvent.click(screen.getByRole('button', {name: 'Confirm'}))

    expect(onConfirm).toHaveBeenCalledTimes(1)
    expect(onOpenChange).toHaveBeenCalledTimes(1)
    expect(onOpenChange).toHaveBeenCalledWith(false)
    // Asserting the sequence, not merely that both fired: a handler that inspects open state must
    // observe the confirming state, not the closed one. Reversing these two lines in the component
    // leaves both call counts correct and only this assertion fails.
    expect(order).toEqual(['confirm', 'close'])
  })

  // confirm-dialog.cancel-closes-without-confirm
  it('closes on cancel without invoking onConfirm', async () => {
    const {onConfirm, onOpenChange} = renderConfirm()
    await userEvent.click(screen.getByRole('button', {name: 'Cancel'}))
    expect(onConfirm).not.toHaveBeenCalled()
    expect(onOpenChange).toHaveBeenCalledTimes(1)
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  // confirm-dialog.localized-labels
  it('resolves both action labels from the catalog', () => {
    renderConfirm({}, {catalog: {'common.confirm': 'Confirm', 'common.cancel': 'Cancel'}})
    expect(screen.getByRole('button', {name: 'Confirm'})).toBeInTheDocument()
    expect(screen.getByRole('button', {name: 'Cancel'})).toBeInTheDocument()
  })

  // confirm-dialog.label-override
  it('prefers explicit labels over the catalog', () => {
    renderConfirm(
      {confirmLabel: 'Delete forever', cancelLabel: 'Keep'},
      {catalog: {'common.confirm': 'Confirm', 'common.cancel': 'Cancel'}},
    )
    expect(screen.getByRole('button', {name: 'Delete forever'})).toBeInTheDocument()
    expect(screen.getByRole('button', {name: 'Keep'})).toBeInTheDocument()
    expect(screen.queryByRole('button', {name: 'Confirm'})).toBeNull()
  })

  // confirm-dialog.key-echo-fallback — the defect class this case exists for.
  it('never renders a raw catalog key when the catalog echoes keys', () => {
    // A catalog that returns the key itself is the observed failure mode; the user sees
    // "common.confirm" in the UI. The module must fall back to readable copy instead.
    renderConfirm({}, {catalog: {'common.confirm': 'common.confirm', 'common.cancel': 'common.cancel'}})
    const labels = screen.getAllByRole('button').map(node => node.textContent ?? '')
    expect(labels.some(label => label.includes('common.confirm'))).toBe(false)
    expect(labels.some(label => label.includes('common.cancel'))).toBe(false)
  })

  // confirm-dialog.destructive-emphasis
  it('changes only emphasis for the destructive variant, not order or semantics', () => {
    const {unmount} = renderConfirm()
    const defaultOrder = screen.getAllByRole('button').map(node => node.textContent)
    const defaultConfirm = screen.getByRole('button', {name: 'Confirm'})
    expect(defaultConfirm.className).not.toContain('hl-confirm-dialog__confirm--destructive')
    unmount()

    renderConfirm({variant: 'destructive'})
    const destructiveConfirm = screen.getByRole('button', {name: 'Confirm'})
    // Emphasis changes...
    expect(destructiveConfirm.className).toContain('hl-confirm-dialog__confirm--destructive')
    // ...while order and accessible semantics do not. A destructive confirmation that silently
    // reorders the action pair trains users to click the wrong control.
    expect(screen.getAllByRole('button').map(node => node.textContent)).toEqual(defaultOrder)
    expect(destructiveConfirm.tagName).toBe('BUTTON')
  })

  // confirm-dialog.dismissal-passthrough
  it('passes dismissal policy through to the composed shell', async () => {
    const {onOpenChange} = renderConfirm({closeOnOverlayClick: false, closeOnEscape: false})
    await userEvent.keyboard('{Escape}')
    expect(onOpenChange).not.toHaveBeenCalled()
  })

  // confirm-dialog.composes-dialog
  it('takes its modal role from the shell and does not own modal chrome', () => {
    renderConfirm()
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    // The role and aria-modal come from hlp.ui.dialog. This module must not emit its own dialog
    // element — a second one would produce two modal roles in the tree, which is the composition
    // defect corrected in wave 04-01.
    expect(screen.getAllByRole('dialog')).toHaveLength(1)
  })

  // confirm-dialog.long-content-reflow
  it('wraps long localized labels rather than overflowing the action pair', () => {
    const long = 'A deliberately expanded pseudo-localized confirmation label that must wrap safely'
    renderConfirm({confirmLabel: long})
    const confirm = screen.getByRole('button', {name: long})
    // The style contract is min-inline-size: 0 plus overflow-wrap: break-word; assert the class is
    // applied so the stylesheet has a hook. Pixel behaviour is the gallery's reflow check, not a
    // jsdom assertion — jsdom does not lay out, so claiming to verify wrapping here would be false.
    expect(confirm.className).toContain('hl-confirm-dialog__confirm')
  })

  // confirm-dialog.locale-rtl
  it('inherits RTL direction from the locale provider', () => {
    renderConfirm({}, {direction: 'rtl', locale: 'ar'})
    // Direction is inherited, never set by this module — mirroring is the shell's and the
    // stylesheet's logical-property job.
    expect(document.querySelector('[dir="rtl"]')).not.toBeNull()
  })

  // confirm-dialog.replacement
  it('renders replaced content without retaining stale copy', () => {
    const {rerender} = renderConfirm({confirmLabel: 'First'})
    expect(screen.getByRole('button', {name: 'First'})).toBeInTheDocument()
    rerender(
      <HarborlineLocaleProvider catalog={{}} direction="ltr" locale="en-US">
        <ConfirmDialog {...baseProps} confirmLabel="Second" onConfirm={vi.fn()} onOpenChange={vi.fn()} open />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('button', {name: 'Second'})).toBeInTheDocument()
    expect(screen.queryByRole('button', {name: 'First'})).toBeNull()
  })

  // confirm-dialog.projection-equivalence
  it('exposes the observable surface a second lane must reproduce', () => {
    renderConfirm({confirmLabel: 'Confirm', cancelLabel: 'Cancel'})
    // The equivalence contract is: one dialog, exactly two actions, cancel first, confirm second.
    // A generated lane is compared against this shape, so it is asserted here rather than left to
    // the visual baseline alone.
    expect(screen.getAllByRole('dialog')).toHaveLength(1)
    // The composed shell contributes its own close-icon button with no text content. Asserting
    // ['Cancel','Confirm'] was wrong about the rendered tree, not about the contract: what this
    // module owns is the ORDER of the two labelled actions, so filter to those.
    const labelled = screen.getAllByRole('button').map(node => node.textContent).filter(Boolean)
    expect(labelled).toEqual(['Cancel', 'Confirm'])
  })

  // confirm-dialog.action-classes
  it('emits exactly the class list the authority stylesheet defines', () => {
    // The spelling of these classes IS the parity: the Blazor lane asserts the same fixture row,
    // so a lane-only class or a dropped modifier turns both suites red instead of hiding in a
    // hand-written lane stylesheet (ticket 282).
    const expected = fixtureCases.find(value => value.id === 'confirm-dialog.action-classes')!
      .expected as Record<string, string[]>
    const {unmount} = renderConfirm()
    expect([...screen.getByRole('button', {name: 'Cancel'}).classList]).toEqual(expected.cancelClasses)
    expect([...screen.getByRole('button', {name: 'Confirm'}).classList]).toEqual(expected.confirmClasses)
    unmount()

    renderConfirm({variant: 'destructive'})
    expect([...screen.getByRole('button', {name: 'Confirm'}).classList]).toEqual(expected.destructiveConfirmClasses)
  })
})
