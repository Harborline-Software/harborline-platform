import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { DateField } from '../index'
import { qualityCases } from './fixtures'

describe('DateField React quality profile', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'date-field.quality.native',
      'date-field.quality.name',
      'date-field.quality.description',
      'date-field.quality.focus',
      'date-field.quality.reflow',
      'date-field.quality.raw-value',
      'date-field.quality.rtl',
      'date-field.quality.pseudo',
      'date-field.quality.light-dark',
      'date-field.quality.tokens',
      'date-field.quality.forced-colors',
      'date-field.quality.visual-parity',
    ])
  })

  it('uses browser-native tab focus and leaves locale formatting browser-owned', async () => {
    render(<DateField name="issued" value="2026-08-09" onChange={vi.fn()} aria-label="Issue date" />)
    const control = screen.getByLabelText('Issue date') as HTMLInputElement
    await userEvent.setup().tab()
    expect(control).toHaveFocus()
    expect(control.type).toBe('date')
    expect(control.value).toBe('2026-08-09')
  })

  it('publishes semantic, logical, reflow-safe, dark, focus, invalid, and forced-color styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-date-field-surface')
    expect(css).toContain('inline-size: 100%')
    expect(css).toContain('max-inline-size: 100%')
    expect(css).toContain('min-inline-size: 0')
    expect(css).toContain('padding-inline: 0.75rem')
    expect(css).toContain('text-align: start')
    expect(css).toContain('.hl-date-field:focus-visible')
    expect(css).toContain('.hl-date-field--invalid')
    expect(css).toContain("[data-theme='dark'] .hl-date-field")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i)
    expect(css).not.toContain('transition:')
  })
})
