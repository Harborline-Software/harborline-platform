import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

import { qualityCases } from './fixtures'

describe('SwitchField React projection quality', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'switch-field.quality.name-state',
      'switch-field.quality.target',
      'switch-field.quality.catalog',
      'switch-field.quality.rtl',
      'switch-field.quality.light-dark',
      'switch-field.quality.tokens',
      'switch-field.quality.visual-parity',
      'switch-field.quality.keyboard',
      'switch-field.quality.focus',
      'switch-field.quality.rtl',
      'switch-field.quality.reduced-motion',
    ])
  })

  it('is a zero-state adapter with no duplicate CSS or provider dependency', () => {
    const source = readFileSync(resolve(import.meta.dirname, '../SwitchField.tsx'), 'utf8')
    expect(source).toContain("from '@harborline-platform/hlp.ui.switch'")
    expect(source).not.toMatch(/React\.useState|React\.useEffect|Telerik|Kendo|Syncfusion/)
    expect(() => readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')).toThrow()
  })
})
