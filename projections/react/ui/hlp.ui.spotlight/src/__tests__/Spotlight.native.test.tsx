import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

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

  // b20: WCAG 4.1.3 — a screen-reader user who types to zero results hears the caller's empty
  // copy from the status region that existed before the change, even without resultsCountLabel.
  it('announces zero results through the pre-existing polite status region', () => {
    const { rerender } = render(<Spotlight {...props} empty="Aucun résultat" />)
    const status = screen.getByRole('status')
    expect(status).toHaveAttribute('aria-live', 'polite')
    expect(status.textContent).toBe('')
    rerender(<Spotlight {...props} empty="Aucun résultat" query="zz" sections={[]} />)
    expect(screen.getByRole('status')).toBe(status)
    expect(status.textContent).toBe('Aucun résultat')
    expect(screen.getByRole('combobox')).toHaveFocus()
  })

  // b20 review: loading with no option announces the loading label (or nothing), never "0 results"
  // followed by the empty copy.
  it('announces no zero count while a section is still loading', () => {
    const count = (n: number) => `${n} résultats`
    const { rerender } = render(<Spotlight {...props} empty="Aucun résultat" resultsCountLabel={count} sections={[{ id: 'r', label: 'Records', items: [], loading: true, loadingLabel: 'Chargement' }]} />)
    expect(screen.getByRole('status').textContent).toBe('Chargement')
    rerender(<Spotlight {...props} empty="Aucun résultat" resultsCountLabel={count} sections={[{ id: 'r', label: 'Records', items: [], loading: true }]} />)
    expect(screen.getByRole('status').textContent).toBe('')
    rerender(<Spotlight {...props} empty="Aucun résultat" resultsCountLabel={count} sections={[]} />)
    expect(screen.getByRole('status').textContent).toBe('Aucun résultat')
  })

  it('does not announce the count beside the empty copy', () => {
    render(<Spotlight {...props} empty="Aucun résultat" resultsCountLabel={count => `${count} résultats`} sections={[]} />)
    expect(screen.getByRole('status').textContent).toBe('Aucun résultat')
  })

  // APG combobox: aria-expanded is true only while the popup is displayed; an empty listbox shows nothing.
  it('reports a collapsed popup while there are no results and no loading section', () => {
    const { rerender } = render(<Spotlight {...props} />)
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-expanded', 'true')
    rerender(<Spotlight {...props} sections={[]} />)
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-controls', screen.getByRole('listbox').id)
    rerender(<Spotlight {...props} sections={[{ id: 'r', label: 'Records', items: [], loading: true, loadingLabel: 'Loading' }]} />)
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-expanded', 'true')
  })

  // review fix: the palette portals to <body>, so a wrapper's dir never reached it (this test once used
  // <div dir="rtl"> and passed without RTL). Direction comes from the locale scope, as in Dialog.
  it('uses caller copy, takes rtl from the locale scope and keeps ranked item order', () => {
    render(<HarborlineLocaleProvider locale="ar-SA"><Spotlight {...props} empty="لا نتائج" resultsCountLabel={count => `${count} نتيجة`} /></HarborlineLocaleProvider>)
    expect(screen.getByRole('dialog', { name: 'Rechercher' })).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('status')).toHaveTextContent('1 نتيجة')
    expect(screen.getAllByRole('option').map(value => value.getAttribute('data-item-id'))).toEqual(['one'])
  })

  it('is ltr with no locale scope, as Dialog is', () => {
    render(<Spotlight {...props} />)
    expect(screen.getByRole('dialog', { name: 'Rechercher' })).toHaveAttribute('dir', 'ltr')
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
