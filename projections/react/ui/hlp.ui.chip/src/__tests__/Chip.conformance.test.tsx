import * as React from 'react'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Chip } from '../Chip'
import { fixture, sharedCases } from './fixtures'

describe('Chip shared fixtures', () => {
  it('chip.content', () => {
    fixture(sharedCases, 'chip.content')
    render(<Chip text="Priority" />)
    expect(screen.getByRole('button', { name: 'Priority' })).toHaveAccessibleName('Priority')
  })

  it('chip.defaults', () => {
    fixture(sharedCases, 'chip.defaults')
    render(<Chip text="Priority" />)
    const chip = screen.getByRole('button', { name: 'Priority' })
    expect(chip).toHaveAttribute('aria-pressed', 'false')
    expect(chip).not.toBeDisabled()
    expect(chip).toHaveAttribute('data-hl-fill-mode', 'solid')
    expect(chip).toHaveAttribute('data-hl-theme-color', 'base')
    expect(chip).toHaveAttribute('data-hl-size', 'md')
    expect(chip).toHaveAttribute('data-hl-rounded', 'full')
    expect(screen.queryByRole('button', { name: /^Remove/ })).toBeNull()
  })

  it('chip.uncontrolled-selection', async () => {
    fixture(sharedCases, 'chip.uncontrolled-selection')
    const onSelectedChange = vi.fn()
    render(<Chip defaultSelected={false} onSelectedChange={onSelectedChange} text="Priority" />)
    const chip = screen.getByRole('button', { name: 'Priority' })
    await userEvent.setup().click(chip)
    expect(chip).toHaveAttribute('aria-pressed', 'true')
    await userEvent.setup().click(chip)
    expect(chip).toHaveAttribute('aria-pressed', 'false')
    expect(onSelectedChange.mock.calls.map(([selected]) => selected)).toEqual([true, false])
  })

  it('chip.controlled-selection', async () => {
    fixture(sharedCases, 'chip.controlled-selection')
    const onSelectedChange = vi.fn()
    render(<Chip onSelectedChange={onSelectedChange} selected={false} text="Priority" />)
    const chip = screen.getByRole('button', { name: 'Priority' })
    await userEvent.setup().click(chip)
    expect(chip).toHaveAttribute('aria-pressed', 'false')
    expect(onSelectedChange).toHaveBeenCalledOnce()
    expect(onSelectedChange).toHaveBeenCalledWith(true)
  })

  it('chip.disabled', async () => {
    fixture(sharedCases, 'chip.disabled')
    const onRemove = vi.fn()
    const onSelectedChange = vi.fn()
    render(<Chip disabled onRemove={onRemove} onSelectedChange={onSelectedChange} removable text="Priority" />)
    const controls = screen.getAllByRole('button')
    expect(controls).toHaveLength(2)
    for (const control of controls) {
      expect(control).toBeDisabled()
      await userEvent.setup().click(control)
    }
    expect(onRemove).not.toHaveBeenCalled()
    expect(onSelectedChange).not.toHaveBeenCalled()
  })

  it('chip.remove', async () => {
    fixture(sharedCases, 'chip.remove')
    const onRemove = vi.fn()
    render(<Chip onRemove={onRemove} removable removeLabel="Remove Priority" text="Priority" />)
    const body = screen.getByRole('button', { name: 'Priority' })
    const remove = screen.getByRole('button', { name: 'Remove Priority' })
    expect(body.parentElement).toBe(remove.parentElement)
    expect(remove).toHaveClass('hl-chip__remove')
    for (const key of ['{Enter}', ' ', '{Backspace}', '{Delete}']) {
      remove.focus()
      await userEvent.setup().keyboard(key)
    }
    expect(onRemove).toHaveBeenCalledTimes(4)
  })

  it('chip.remove-no-toggle', async () => {
    fixture(sharedCases, 'chip.remove-no-toggle')
    const onRemove = vi.fn()
    const onSelectedChange = vi.fn()
    const bubbled = vi.fn()
    render(<div onClick={bubbled}><Chip onRemove={onRemove} onSelectedChange={onSelectedChange} removable text="Priority" /></div>)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Remove Priority' }))
    expect(onRemove).toHaveBeenCalledOnce()
    expect(onSelectedChange).not.toHaveBeenCalled()
    expect(bubbled).not.toHaveBeenCalled()
  })

  it('chip.noninteractive', () => {
    fixture(sharedCases, 'chip.noninteractive')
    render(<Chip selectable={false} text="Priority" />)
    expect(screen.queryByRole('button')).toBeNull()
    expect(screen.getByText('Priority').tagName).toBe('SPAN')
    expect(screen.getByText('Priority')).not.toHaveAttribute('aria-pressed')
  })

  it('chip.icon-avatar', () => {
    fixture(sharedCases, 'chip.icon-avatar')
    const { container } = render(<Chip avatar="person.png" icon={<svg data-testid="priority-icon" />} text="Priority" />)
    const body = screen.getByRole('button', { name: 'Priority' })
    const avatar = container.querySelector('img')
    const icon = screen.getByTestId('priority-icon').parentElement
    expect(avatar).toHaveAttribute('alt', '')
    expect(icon).toHaveAttribute('aria-hidden', 'true')
    expect([...body.children].map(element => element.className)).toEqual([
      'hl-chip__avatar',
      'hl-chip__icon',
      'hl-chip__text',
    ])
  })

  it('chip.style-axes', () => {
    fixture(sharedCases, 'chip.style-axes')
    const { rerender } = render(<Chip fillMode="outline" rounded="small" size="lg" text="Priority" themeColor="warning" />)
    const chip = screen.getByRole('button')
    expect(chip).toHaveAttribute('data-hl-fill-mode', 'outline')
    expect(chip).toHaveAttribute('data-hl-theme-color', 'warning')
    expect(chip).toHaveAttribute('data-hl-size', 'lg')
    expect(chip).toHaveAttribute('data-hl-rounded', 'small')
    rerender(<Chip size="large" text="Priority" />)
    expect(screen.getByRole('button')).toHaveAttribute('data-hl-size', 'lg')
  })

  it('chip.rtl', async () => {
    fixture(sharedCases, 'chip.rtl')
    const onSelectedChange = vi.fn()
    render(
      <HarborlineLocaleProvider locale="ar-SA">
        <Chip onSelectedChange={onSelectedChange} removable text="الأولوية" />
      </HarborlineLocaleProvider>,
    )
    const chip = screen.getByRole('button', { name: 'الأولوية' })
    expect(chip.closest('.hl-chip')).toHaveAttribute('dir', 'rtl')
    await userEvent.setup().click(chip)
    expect(onSelectedChange).toHaveBeenCalledWith(true)
  })

  it('chip.invalid-input', () => {
    fixture(sharedCases, 'chip.invalid-input')
    expect(() => render(<Chip accessibleLabel="" text="" />)).toThrow('accessible-content-required')
  })
})
