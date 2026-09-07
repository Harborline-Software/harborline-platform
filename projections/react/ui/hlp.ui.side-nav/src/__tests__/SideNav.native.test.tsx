import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { SideNav } from '../SideNav'
import { qualityCases } from './fixtures'

describe('SideNav React projection quality', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'side-nav.quality.landmark-state',
      'side-nav.quality.targets-reflow',
      'side-nav.quality.catalog',
      'side-nav.quality.pseudo',
      'side-nav.quality.light-dark',
      'side-nav.quality.tokens',
      'side-nav.quality.forced-colors',
      'side-nav.quality.visual-parity',
      'side-nav.quality.keyboard',
      'side-nav.quality.focus',
      'side-nav.quality.tooltip-dismissal',
      'side-nav.quality.rtl',
      'side-nav.quality.reduced-motion',
    ])
  })

  it('uses instance, catalog, and fallback navigation-name precedence', () => {
    const rendered = render(<SideNav items={[]} />)
    expect(screen.getByRole('navigation', { name: 'Navigation' })).toBeInTheDocument()
    rendered.rerender(
      <HarborlineLocaleProvider catalog={{ 'navigation.label': '[!! Navigation expanded !!]' }}>
        <SideNav items={[]} />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('navigation', { name: '[!! Navigation expanded !!]' })).toBeInTheDocument()
    rendered.rerender(<SideNav items={[]} navigationLabel="Primary workspace" />)
    expect(screen.getByRole('navigation', { name: 'Primary workspace' })).toBeInTheDocument()
  })

  it('publishes logical, tokenized, target, theme, forced-color, and motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-side-nav-surface')
    expect(css).toContain('padding-inline')
    expect(css).toContain('margin-inline-start')
    expect(css).toContain('min-block-size: 2.25rem')
    expect(css).toContain('min-block-size: 44px')
    expect(css).toContain("[data-theme='dark'] .hl-side-nav")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).not.toContain('transition: all')
  })
})
