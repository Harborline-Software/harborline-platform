import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { NumericTextBox } from '../NumericTextBox'
import { qualityCases } from './fixtures'

describe('NumericTextBox React projection quality', () => {
  it('consumes every non-performance quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'numeric-text-box.quality.naming-state',
      'numeric-text-box.quality.targets',
      'numeric-text-box.quality.reflow',
      'numeric-text-box.quality.number-formats',
      'numeric-text-box.quality.catalog',
      'numeric-text-box.quality.pseudo',
      'numeric-text-box.quality.light-dark',
      'numeric-text-box.quality.tokens',
      'numeric-text-box.quality.forced-colors',
      'numeric-text-box.quality.visual-parity',
      'numeric-text-box.quality.enter',
      'numeric-text-box.quality.spinner-equivalent',
      'numeric-text-box.quality.focus-buffer',
      'numeric-text-box.quality.callback-order',
      'numeric-text-box.quality.rtl',
      'numeric-text-box.quality.reduced-motion',
      'numeric-text-box.quality.controlled-uncontrolled',
    ])
  })

  it('keeps disabled controls inert and readonly controls spinner-free and non-emitting', () => {
    const onChange = vi.fn()
    const rendered = render(<NumericTextBox aria-label="Amount" disabled onChange={onChange} value={4} />)
    expect(screen.getByRole('textbox')).toBeDisabled()
    expect(screen.getAllByRole('button')).toHaveLength(2)
    expect(screen.getAllByRole('button').every(button => button.hasAttribute('disabled'))).toBe(true)

    rendered.rerender(<NumericTextBox aria-label="Amount" onChange={onChange} readOnly value={4} />)
    const input = screen.getByRole('textbox')
    expect(input).toHaveAttribute('readonly')
    expect(screen.queryAllByRole('button')).toHaveLength(0)
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '9' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    fireEvent.blur(input)
    expect(onChange).not.toHaveBeenCalled()
  })

  it('provides keyboard-equivalent stepping from the textbox and focusable buttons', async () => {
    const onChange = vi.fn()
    render(<NumericTextBox aria-label="Amount" onChange={onChange} value={4} />)
    const input = screen.getByRole('textbox')
    fireEvent.keyDown(input, { key: 'ArrowUp' })
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    expect(onChange.mock.calls.map(call => call[0])).toEqual([5, 3])

    const user = userEvent.setup()
    await user.tab()
    expect(input).toHaveFocus()
    await user.tab()
    expect(screen.getByRole('button', { name: 'Increment' })).toHaveFocus()
  })

  it('keeps external controlled replacement out of the active raw buffer', () => {
    const rendered = render(<NumericTextBox aria-label="Amount" value={2} />)
    const input = screen.getByRole('textbox')
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '12.5' } })
    rendered.rerender(<NumericTextBox aria-label="Amount" value={9} />)
    expect(input).toHaveValue('12.5')
    fireEvent.blur(input)
    expect(input).toHaveValue('9.00')
  })

  it('publishes logical, reflow-safe, tokenized, dark, forced-color, focus, and reduced-motion CSS', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-numeric-text-box-surface')
    expect(css).toContain('min-inline-size: 0')
    expect(css).toContain('padding-inline')
    expect(css).toContain('border-inline-start')
    expect(css).toContain('min-block-size: 2.5rem')
    expect(css).toContain('@media (any-pointer: coarse)')
    expect(css).toContain('min-inline-size: 44px')
    expect(css).toContain(':focus-visible')
    expect(css).toContain("[data-theme='dark'] .hl-numeric-text-box")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).not.toContain('transition: all')
  })

  it('keeps public source provider-neutral', () => {
    const types = readFileSync(resolve(import.meta.dirname, '../NumericTextBox.types.ts'), 'utf8')
    const entry = readFileSync(resolve(import.meta.dirname, '../index.ts'), 'utf8')
    expect(`${types}\n${entry}`).not.toMatch(/Telerik|Kendo|Syncfusion|Intl\.NumberFormat/)
  })
})
