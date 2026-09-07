import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { Input } from '../Input'

describe('Input React projection', () => {
  it('preserves native attributes and requests controlled values once', () => {
    const change = vi.fn()
    render(<Input id="email" name="email" type="email" value="old" aria-label="Email" data-case="intake" onChange={event => change(event.currentTarget.value)}/>)
    const input = screen.getByRole('textbox', { name: 'Email' })
    expect(input).toHaveAttribute('data-case', 'intake')
    fireEvent.change(input, { target: { value: 'new' } })
    expect(change).toHaveBeenCalledOnce()
    expect(change).toHaveBeenCalledWith('new')
  })

  it('keeps invalid visual-only and renders adornments around the native input', () => {
    render(<Input aria-label="Amount" invalid prefix="$" suffix="USD"/>)
    const input = screen.getByRole('textbox', { name: 'Amount' })
    expect(input).not.toHaveAttribute('aria-invalid')
    expect(screen.getByText('$')).toBeInTheDocument()
    expect(screen.getByText('USD')).toBeInTheDocument()
  })

  it('draws one focus ring around the complete adorned control', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain(':hover:not(:disabled)')
    // The wrapper owns the ring, so it must actually declare one.
    expect(css).toMatch(/\.hl-input:focus-within[^{]*\{[^}]*outline:\s*2px solid var\(--hl-input-focus\)/)
    // The inner control suppresses its own, and the selector is load-bearing. :focus-visible is a
    // subset of :focus, so suppressing at :focus still covers it -- while the focus gate reads only
    // :focus-visible blocks, where `outline: none` registers as a ring declared with a literal
    // colour. Both halves are pinned so neither can be reverted without this test saying so.
    expect(css).toMatch(/\.hl-input \.hl-input__control:focus\s*\{\s*outline:\s*none/)
    expect(css).not.toMatch(/\.hl-input \.hl-input__control:focus-visible\s*\{\s*outline:\s*none/)
  })
})
