import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

import { qualityCases } from './fixtures'

describe('TextBox React projection quality', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'text-box.quality.name-state',
      'text-box.quality.actions-targets',
      'text-box.quality.reflow',
      'text-box.quality.catalog',
      'text-box.quality.pseudo',
      'text-box.quality.light-dark',
      'text-box.quality.tokens',
      'text-box.quality.forced-colors',
      'text-box.quality.visual-parity',
      'text-box.quality.keyboard',
      'text-box.quality.focus',
      'text-box.quality.rtl',
      'text-box.quality.reduced-motion',
    ])
  })

  it('publishes logical, reflow-safe, target, theme, forced-color, focus, and motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('min-inline-size: 0')
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain('inline-size: 44px')
    expect(css).toContain(':focus-visible')
    expect(css).toContain("[data-theme='dark'] .hl-text-box__action")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).not.toContain('transition: all')
  })

  it('keeps the public source provider-neutral and composes the shared Input', () => {
    const source = readFileSync(resolve(import.meta.dirname, '../TextBox.tsx'), 'utf8')
    expect(source).toContain("from '@harborline-platform/hlp.ui.input'")
    expect(source).not.toMatch(/Telerik|Kendo|Syncfusion/)
  })
})
