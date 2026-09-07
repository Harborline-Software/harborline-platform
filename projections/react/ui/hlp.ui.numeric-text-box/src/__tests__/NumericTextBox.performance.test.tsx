import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { NumericTextBox } from '../NumericTextBox'
import { fixture, performanceCases } from './fixtures'

describe('NumericTextBox deterministic Tier-C evidence', () => {
  it('numeric-text-box.quality.large-data keeps constant structure for a 256-character raw buffer', () => {
    fixture(performanceCases, 'numeric-text-box.quality.large-data')
    const onChange = vi.fn()
    render(<NumericTextBox aria-label="Amount" onChange={onChange} />)
    const input = screen.getByRole('textbox')
    const raw = '1'.repeat(256)
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: raw } })
    expect(input).toHaveValue(raw)
    expect(document.querySelectorAll('input')).toHaveLength(1)
    expect(screen.getAllByRole('button')).toHaveLength(2)
    expect(onChange).not.toHaveBeenCalled()
  })

  it('numeric-text-box.quality.repeated-update retains only replacement 96 without stale callbacks', () => {
    fixture(performanceCases, 'numeric-text-box.quality.repeated-update')
    const onChange = vi.fn()
    const rendered = render(<NumericTextBox aria-label="Amount 0" onChange={onChange} value={0} />)
    for (let revision = 1; revision <= 96; revision += 1) {
      rendered.rerender(<NumericTextBox aria-label={`Amount ${revision}`} onChange={onChange} value={revision} />)
    }
    expect(screen.getByRole('textbox', { name: 'Amount 96' })).toHaveValue('96.00')
    expect(document.querySelectorAll('input')).toHaveLength(1)
    expect(screen.getAllByRole('button')).toHaveLength(2)
    expect(onChange).not.toHaveBeenCalled()
  })
})
