import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { Separator } from '../Separator'

describe('Separator React projection', () => {
  it('keeps decorative separators out of the accessibility tree', () => {
    const { container } = render(<Separator orientation="vertical" />)
    const separator = container.firstElementChild
    expect(separator).toHaveAttribute('role', 'none')
    expect(separator).not.toHaveAttribute('aria-orientation')
    expect(screen.queryByRole('separator')).toBeNull()
  })

  it('does not derive an accessible name from the visible label', () => {
    render(<Separator decorative={false} label="or" />)
    const separator = screen.getByRole('separator')
    expect(separator).not.toHaveAccessibleName()
    expect(separator.querySelector('.hl-separator__label')).toHaveAttribute('aria-hidden', 'true')
    expect(separator.querySelectorAll('.hl-separator__rule[aria-hidden="true"]')).toHaveLength(2)
  })

  it('preserves localized and pseudolocalized labels without trimming their value', () => {
    const label = '  [!! أو — Choose another route with a deliberately long label !!]  '
    const { container } = render(<Separator label={label} dir="rtl" lang="ar" />)
    const separator = container.firstElementChild
    expect(separator).toHaveAttribute('dir', 'rtl')
    expect(separator).toHaveAttribute('lang', 'ar')
    expect(separator?.querySelector('.hl-separator__label')?.textContent).toBe(label)
  })

  it('uses logical layout and tokenized forced-color-safe styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-separator-rule')
    expect(css).toContain('--hl-separator-label')
    expect(css).toContain('inline-size')
    expect(css).toContain('block-size')
    expect(css).not.toMatch(
      /^\s*(left|right|width|height|margin-left|margin-right|padding-left|padding-right)\s*:/m,
    )
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain('@media (forced-colors: active)')
  })
})
