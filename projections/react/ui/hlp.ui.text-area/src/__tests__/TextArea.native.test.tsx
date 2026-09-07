import { createRef, useState } from 'react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { FormFieldContext } from '@harborline-platform/hlp.ui.form-field-context'
import { TextArea } from '../TextArea'
import { qualityCases } from './fixtures'

describe('TextArea React projection', () => {
  it('consumes every frozen quality case', () => {
    expect([...new Set(qualityCases.map(value => value.id))]).toEqual([
      'text-area.quality.native',
      'text-area.quality.relationships',
      'text-area.quality.counter',
      'text-area.quality.focus',
      'text-area.quality.reflow',
      'text-area.quality.raw-copy',
      'text-area.quality.rtl',
      'text-area.quality.pseudo',
      'text-area.quality.light-dark',
      'text-area.quality.tokens',
      'text-area.quality.forced-colors',
      'text-area.quality.reduced-motion',
      'text-area.quality.visual-parity',
      'text-area.quality.keyboard',
    ])
  })

  it('preserves native multiline keyboard editing and exact raw strings', async () => {
    const observed = vi.fn()
    function Harness() {
      const [value, setValue] = useState('')
      return <TextArea aria-label="Notes" onChange={next => { observed(next); setValue(next) }} value={value} />
    }

    render(<Harness />)
    const control = screen.getByRole('textbox', { name: 'Notes' })
    await userEvent.setup().type(control, '  café{enter}مرحبا  ')
    expect(control).toHaveValue('  café\nمرحبا  ')
    expect(observed).toHaveBeenLastCalledWith('  café\nمرحبا  ')
  })

  it('merges descriptions without dangling duplicates and preserves caller aria state', () => {
    render(
      <FormFieldContext.Provider value={{ labelId: 'notes-label', describedBy: 'notes-hint' }}>
        <span id="notes-label">Notes</span>
        <span id="notes-hint">Hint</span>
        <span id="consumer-help">Consumer help</span>
        <TextArea aria-describedby="consumer-help notes-hint" aria-invalid="grammar" />
      </FormFieldContext.Provider>,
    )
    const control = screen.getByRole('textbox', { name: 'Notes' })
    expect(control).toHaveAttribute('aria-describedby', 'notes-hint consumer-help')
    expect(control).toHaveAttribute('aria-invalid', 'grammar')
  })

  it('forwards the native textarea ref and does not add the visual counter to its accessible description', () => {
    const ref = createRef<HTMLTextAreaElement>()
    render(<TextArea aria-label="Notes" ref={ref} showCounter value="abc" />)
    const control = screen.getByRole('textbox', { name: 'Notes' })
    expect(ref.current).toBe(control)
    expect(control).not.toHaveAttribute('aria-describedby')
    expect(document.querySelector('.hl-text-area__counter')).toHaveAttribute('aria-hidden', 'true')
  })

  it('uses logical, reflow-safe, token, dark-theme, forced-color, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-text-area-surface')
    expect(css).toContain('min-inline-size: 0')
    expect(css).toContain('inset-inline-end')
    expect(css).toContain('text-align: start')
    expect(css).toContain(':focus-visible')
    expect(css).toContain("[data-theme='dark'] .hl-text-area__root")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/prefers-reduced-motion:[\s\S]*transition: none/)
  })
})
