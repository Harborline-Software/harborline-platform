import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Spotlight } from '../Spotlight'
import * as publicSurface from '../index'
import { qualityCases } from './fixtures'

const props = {
  ariaLabel: 'Rechercher',
  onOpenChange: vi.fn(),
  onQueryChange: vi.fn(),
  open: true,
  query: '',
  sections: [{ id: 'actions', label: 'Actions', items: [{ id: 'one', label: 'Ouvrir', onSelect: vi.fn() }] }],
}

describe('Spotlight React projection', () => {
  it('consumes every frozen quality case', () => {
    expect([...new Set(qualityCases.map(value => value.id))]).toEqual([
      'spotlight.quality.combobox', 'spotlight.quality.groups', 'spotlight.quality.status',
      'spotlight.quality.keyboard', 'spotlight.quality.focus', 'spotlight.quality.reflow',
      'spotlight.quality.caller-copy', 'spotlight.quality.rtl', 'spotlight.quality.pseudo',
      'spotlight.quality.light-dark', 'spotlight.quality.tokens', 'spotlight.quality.forced-colors',
      'spotlight.quality.reduced-motion', 'spotlight.quality.visual-parity', 'spotlight.quality.dismissal',
    ])
  })

  it('publishes only the frozen runtime export', () => {
    expect(Object.keys(publicSurface)).toEqual(['Spotlight'])
  })

  it('traps focus in both directions', async () => {
    render(<><button type="button">Outside</button><Spotlight {...props} footer={<button type="button">Toutes</button>} /></>)
    const input = screen.getByRole('combobox')
    const footer = screen.getByRole('button', { name: 'Toutes' })
    const outside = screen.getByRole('button', { name: 'Outside' })
    expect(input).toHaveFocus()
    outside.focus()
    expect(input).toHaveFocus()
    await userEvent.setup().tab({ shift: true })
    expect(footer).toHaveFocus()
    await userEvent.setup().tab()
    expect(input).toHaveFocus()
  })

  it('requests one dismissal for one scrim interaction', () => {
    const onOpenChange = vi.fn()
    render(<Spotlight {...props} onOpenChange={onOpenChange} />)
    fireEvent.mouseDown(screen.getByTestId('spotlight-scrim'))
    expect(onOpenChange).toHaveBeenCalledTimes(1)
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('uses caller copy and keeps ranked item order under RTL', () => {
    render(<div dir="rtl"><Spotlight {...props} empty="لا نتائج" resultsCountLabel={count => `${count} نتيجة`} /></div>)
    expect(screen.getByRole('dialog', { name: 'Rechercher' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('1 نتيجة')
    expect(screen.getAllByRole('option').map(value => value.getAttribute('data-item-id'))).toEqual(['one'])
  })

  it('publishes reflow, logical, token, dark, forced-color, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-spotlight-surface')
    expect(css).toContain('max-inline-size')
    expect(css).toContain('max-block-size')
    expect(css).toContain('inset-inline-start')
    expect(css).toContain('text-align: start')
    expect(css).toContain("[data-theme='dark'] .hl-spotlight")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/prefers-reduced-motion:[\s\S]*transition: none/)
  })
})
