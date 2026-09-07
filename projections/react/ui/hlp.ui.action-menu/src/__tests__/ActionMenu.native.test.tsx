import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { ActionMenu } from '../ActionMenu'
import { qualityCases } from './fixtures'

describe('ActionMenu React projection quality', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'action-menu.quality.pattern',
      'action-menu.quality.keyboard',
      'action-menu.quality.focus',
      'action-menu.quality.dismissal',
      'action-menu.quality.forced-colors',
      'action-menu.quality.reflow',
      'action-menu.quality.locales',
      'action-menu.quality.rtl',
      'action-menu.quality.pseudo',
      'action-menu.quality.themes',
      'action-menu.quality.tokens',
      'action-menu.quality.contrast',
      'action-menu.quality.reduced-motion',
      'action-menu.quality.visual-parity',
    ])
  })

  it('resolves default trigger copy from the locale catalog', () => {
    render(
      <HarborlineLocaleProvider locale="en-XA" catalog={{ 'buttons.moreActions': '[!! More actions expanded !!]' }}>
        <ActionMenu items={[{ label: 'Edit', onClick: vi.fn() }]} />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('button', { name: '[!! More actions expanded !!]' })).toBeInTheDocument()
  })

  it('rejects an empty effective default trigger name', () => {
    expect(() => render(
      <HarborlineLocaleProvider catalog={{ 'buttons.moreActions': ' ' }}>
        <ActionMenu items={[]} />
      </HarborlineLocaleProvider>,
    )).toThrow('accessible-name-required')
  })

  it('styles a custom trigger while preserving its caller class and accessible name', () => {
    render(<ActionMenu trigger={<button className="consumer">Review certification</button>} items={[]} />)
    expect(screen.getByRole('button', { name: 'Review certification' })).toHaveClass(
      'hl-action-menu__trigger',
      'hl-action-menu__trigger--custom',
      'consumer',
    )
  })

  it('publishes logical, token, theme, forced-color, reflow, and motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-action-menu-surface')
    expect(css).toContain("[data-hl-align='left']")
    expect(css).toContain('inset-inline-start')
    expect(css).toContain('inset-inline-end')
    expect(css).toContain('max-inline-size: calc(100vi - 2rem)')
    expect(css).toContain("[data-theme='dark'] .hl-action-menu")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
  })
})
