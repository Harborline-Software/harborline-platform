import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Chip } from '../Chip'
import { qualityCases } from './fixtures'

describe('Chip React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'chip.quality.pattern',
      'chip.quality.keyboard',
      'chip.quality.focus',
      'chip.quality.targets',
      'chip.quality.forced-colors',
      'chip.quality.reflow',
      'chip.quality.locales',
      'chip.quality.rtl',
      'chip.quality.pseudo',
      'chip.quality.themes',
      'chip.quality.tokens',
      'chip.quality.contrast',
      'chip.quality.reduced-motion',
      'chip.quality.visual-parity',
    ])
  })

  it('keeps body and remove controls as sibling keyboard stops', async () => {
    render(<Chip removable text="Priority" />)
    const [body, remove] = screen.getAllByRole('button')
    await userEvent.setup().tab()
    expect(body).toHaveFocus()
    await userEvent.setup().tab()
    expect(remove).toHaveFocus()
    expect(body.contains(remove)).toBe(false)
  })

  it('localizes the remove label and uses provider direction', () => {
    render(
      <HarborlineLocaleProvider
        catalog={{ 'feedback.removeItem': 'إزالة {label}' }}
        locale="ar-SA"
      >
        <Chip removable text="الأولوية" />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('button', { name: 'إزالة الأولوية' })).toBeInTheDocument()
    expect(document.querySelector('.hl-chip')).toHaveAttribute('dir', 'rtl')
  })

  it('uses native toggle activation exactly once', async () => {
    const onSelectedChange = vi.fn()
    render(<Chip onSelectedChange={onSelectedChange} text="Priority" />)
    const chip = screen.getByRole('button', { name: 'Priority' })
    chip.focus()
    await userEvent.setup().keyboard('{Enter}')
    expect(onSelectedChange).toHaveBeenCalledOnce()
  })

  it('publishes appearance tokens, logical layout, focus, theme, forced-color, reflow, and motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-chip-surface')
    expect(css).toContain('--hl-chip-foreground')
    expect(css).toContain('padding-inline')
    expect(css).toContain('min-inline-size: 1.5rem')
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain(':focus-visible')
    expect(css).toContain("[data-theme='dark'] .hl-chip")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
  })

  it('exports only the bounded Chip seam', async () => {
    const publicModule = await import('../index')
    expect(Object.keys(publicModule)).toEqual(['Chip'])
    expect(publicModule).not.toHaveProperty('ChipList')
    expect(publicModule).not.toHaveProperty('ChipRemoveButton')
  })
})
