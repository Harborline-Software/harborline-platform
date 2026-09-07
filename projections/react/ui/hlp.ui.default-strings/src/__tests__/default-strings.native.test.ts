import { createHash } from 'node:crypto'
import { describe, expect, it } from 'vitest'
import { defaultStrings, interpolate, type HarborlineStringCatalog } from '../catalog'

function digestCatalog(): string {
  return createHash('sha256')
    .update(JSON.stringify(Object.entries(defaultStrings)))
    .digest('hex')
}

describe('default strings native behavior', () => {
  it('preserves the canonical 713-key catalog and insertion-order digest', () => {
    expect(Object.keys(defaultStrings)).toHaveLength(713)
    expect(digestCatalog()).toBe('73c7cec0b07f0058d0a48784e76fe8aa86f8e421f85716ff2ea9cc53975c3f7c')
  })

  it('supports partial catalogs without discarding empty-string overrides', () => {
    const catalog: HarborlineStringCatalog = {
      'common.loading': 'Chargement',
      'feedback.retry': '',
    }
    expect(catalog['common.loading'] ?? defaultStrings['common.loading']).toBe('Chargement')
    expect(catalog['feedback.retry'] ?? defaultStrings['feedback.retry']).toBe('')
  })

  it('interpolates string and numeric values while preserving missing placeholders', () => {
    expect(interpolate('Step {current} of {total}', { current: 2 })).toBe('Step 2 of {total}')
    expect(interpolate('Remove {label}', { label: 'Invoice' })).toBe('Remove Invoice')
    expect(interpolate('Page {page}', { page: 12 })).toBe('Page 12')
  })

  it('returns the original template when variables are omitted', () => {
    expect(interpolate('Loading')).toBe('Loading')
  })
})
