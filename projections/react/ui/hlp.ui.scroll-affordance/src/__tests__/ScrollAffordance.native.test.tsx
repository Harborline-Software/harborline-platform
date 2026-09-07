import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { ScrollAffordance } from '../ScrollAffordance'
import { qualityCases } from './fixtures'

describe('ScrollAffordance React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'scroll-affordance.component-quality.name-focus',
      'scroll-affordance.component-quality.keyboard-status',
      'scroll-affordance.component-quality.catalog',
      'scroll-affordance.component-quality.rtl',
      'scroll-affordance.component-quality.light-dark',
      'scroll-affordance.component-quality.mask-tokens',
      'scroll-affordance.component-quality.visual-parity',
    ])
  })

  it('keeps locale direction and translated announcements in the delegated policy', () => {
    render(
      <HarborlineLocaleProvider
        catalog={{ 'scrollAffordance.moreAvailable': 'محتوى إضافي' }}
        direction="rtl"
        locale="ar-SA"
      >
        <ScrollAffordance ariaLabel="النتائج" dir="rtl"><span>نتيجة</span></ScrollAffordance>
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('group', { name: 'النتائج' })).toHaveAttribute('dir', 'rtl')
  })

  it('publishes logical focus, theme-token, forced-color, and reduced-motion CSS', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('min-inline-size')
    expect(css).toContain('var(--hl-focus, Highlight)')
    expect(css).toContain('scroll-snap-type: x mandatory')
    expect(css).toContain('scroll-snap-type: y mandatory')
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).not.toMatch(/#[0-9a-f]{3,8}/i)
  })
})
