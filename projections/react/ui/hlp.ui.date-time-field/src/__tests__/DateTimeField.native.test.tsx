import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { DateTimeField } from '../index'
import { qualityCases } from './fixtures'

describe('DateTimeField React quality profile', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'date-time-field.quality.native',
      'date-time-field.quality.name',
      'date-time-field.quality.description',
      'date-time-field.quality.focus',
      'date-time-field.quality.reflow',
      'date-time-field.quality.raw-value',
      'date-time-field.quality.rtl',
      'date-time-field.quality.pseudo',
      'date-time-field.quality.light-dark',
      'date-time-field.quality.tokens',
      'date-time-field.quality.forced-colors',
      'date-time-field.quality.visual-parity',
    ])
  })

  it('uses browser-native tab focus without timezone reinterpretation', async () => {
    render(
      <DateTimeField
        name="starts"
        value="2026-11-01T01:30"
        onChange={vi.fn()}
        aria-label="Start time"
      />,
    )
    const control = screen.getByLabelText('Start time') as HTMLInputElement
    await userEvent.setup().tab()
    expect(control).toHaveFocus()
    expect(control.type).toBe('datetime-local')
    expect(control.value).toBe('2026-11-01T01:30')
  })

  it('publishes semantic, logical, reflow-safe, dark, focus, invalid, and forced-color styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-date-time-field-surface')
    expect(css).toContain('inline-size: 100%')
    expect(css).toContain('max-inline-size: 100%')
    expect(css).toContain('min-inline-size: 0')
    expect(css).toContain('padding-inline: 0.75rem')
    expect(css).toContain('text-align: start')
    expect(css).toContain('.hl-date-time-field:focus-visible')
    expect(css).toContain('.hl-date-time-field--invalid')
    expect(css).toContain("[data-theme='dark'] .hl-date-time-field")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i)
    expect(css).not.toContain('transition:')
  })
})
