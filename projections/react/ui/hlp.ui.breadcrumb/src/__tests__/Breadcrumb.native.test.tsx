import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { Breadcrumb } from '../Breadcrumb'
import { qualityCases } from './fixtures'

describe('Breadcrumb React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'breadcrumb.quality.landmark',
      'breadcrumb.quality.current',
      'breadcrumb.quality.reflow',
      'breadcrumb.quality.focus',
      'breadcrumb.quality.caller-labels',
      'breadcrumb.quality.rtl',
      'breadcrumb.quality.pseudo',
      'breadcrumb.quality.light-dark',
      'breadcrumb.quality.tokens',
      'breadcrumb.quality.forced-colors',
      'breadcrumb.quality.visual-parity',
    ])
  })

  it('keeps linked ancestors keyboard reachable in trail order', async () => {
    render(<Breadcrumb items={[
      { label: 'Home', href: '/' },
      { label: 'Forms', href: '/forms' },
      { label: 'Edit' },
    ]} />)
    const links = screen.getAllByRole('link')
    await userEvent.setup().tab()
    expect(links[0]).toHaveFocus()
    await userEvent.setup().tab()
    expect(links[1]).toHaveFocus()
    expect(screen.getByText('Edit')).not.toHaveAttribute('href')
  })

  it('uses a stable fallback when the supplied landmark label is blank', () => {
    render(<Breadcrumb accessibleLabel="   " items={[{ id: 'a', label: 'A' }]} />)
    expect(screen.getByRole('navigation')).toHaveAccessibleName('Breadcrumb')
  })

  it('publishes wrapping, logical RTL, token, focus, dark, and forced-color styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-breadcrumb-foreground')
    expect(css).toMatch(/flex-wrap:\s*wrap/)
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain(':dir(rtl) .hl-breadcrumb__separator-icon')
    expect(css).toMatch(/:focus-visible\s*\{[\s\S]*outline: 2px/)
    expect(css).toContain("[data-theme='dark'] .hl-breadcrumb")
    expect(css).toContain('@media (forced-colors: active)')
  })
})
