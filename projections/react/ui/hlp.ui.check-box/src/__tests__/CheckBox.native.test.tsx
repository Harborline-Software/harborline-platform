import { createRef } from 'react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { CheckBox } from '../CheckBox'
import { qualityCases } from './fixtures'

describe('CheckBox React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'check-box.quality.native-control',
      'check-box.quality.label',
      'check-box.quality.keyboard',
      'check-box.quality.touch-target',
      'check-box.quality.focus',
      'check-box.quality.reflow',
      'check-box.quality.caller-label',
      'check-box.quality.rtl',
      'check-box.quality.pseudo',
      'check-box.quality.light-dark',
      'check-box.quality.tokens',
      'check-box.quality.forced-colors',
      'check-box.quality.reduced-motion',
      'check-box.quality.visual-parity',
    ])
  })

  it('uses native space-key activation and emits once', async () => {
    const onChange = vi.fn()
    render(<CheckBox aria-label="Include" onChange={onChange} />)
    const control = screen.getByRole('checkbox')
    control.focus()
    await userEvent.setup().keyboard(' ')
    expect(control).toBeChecked()
    expect(onChange).toHaveBeenCalledOnce()
  })

  it('forwards the input ref and native form binding', () => {
    const ref = createRef<HTMLInputElement>()
    render(<CheckBox ref={ref} aria-label="Include" form="settings" name="include" />)
    expect(ref.current).toBe(screen.getByRole('checkbox'))
    expect(ref.current).toHaveAttribute('form', 'settings')
  })

  it('publishes touch-target, logical, token, state, forced-color, and motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-check-box-surface')
    expect(css).toMatch(/min-block-size:\s*1\.5rem/)
    expect(css).toMatch(/min-inline-size:\s*1\.5rem/)
    expect(css).toContain(':indeterminate')
    expect(css).toContain(':active:not(:disabled)')
    expect(css).toMatch(/:checked::after\s*\{[\s\S]*block-size:\s*0\.52em;[\s\S]*inline-size:\s*0\.28em;/)
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain("[data-theme='dark'] .hl-check-box")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
  })
})
