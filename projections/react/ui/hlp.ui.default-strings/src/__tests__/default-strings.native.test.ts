import { createHash } from 'node:crypto'
import { describe, expect, it } from 'vitest'
import { defaultStrings, interpolate, type HarborlineStringCatalog } from '../catalog'

function digestCatalog(): string {
  return createHash('sha256')
    .update(JSON.stringify(Object.entries(defaultStrings)))
    .digest('hex')
}

describe('default strings native behavior', () => {
  it('preserves the canonical 736-key revision-2 catalog and insertion-order digest', () => {
    expect(Object.keys(defaultStrings)).toHaveLength(736)
    expect(digestCatalog()).toBe('1c7fe490fbbf576290d6f932f8809cbbea21b31800e537f61bd16b712c95f98b')
  })

  it('keeps revision 1 exactly: its 713 keys, by identity and in order, reproduce the revision-1 digest', () => {
    const additions = new Set(["forms.switch.off", "forms.switch.on", "navigation.layersRail.collapse", "navigation.layersRail.empty", "navigation.layersRail.exitLens", "navigation.layersRail.expand", "navigation.layersRail.insert", "navigation.layersRail.lenses", "navigation.layersRail.outline", "navigation.layersRail.region", "navigation.notifications.all", "navigation.notifications.confirm", "navigation.notifications.deny", "navigation.notifications.dismiss", "navigation.notifications.empty", "navigation.notifications.groups", "navigation.notifications.markAllRead", "navigation.notifications.title", "navigation.notifications.unread", "navigation.notifications.viewAll", "navigation.userMenu.panel", "navigation.userMenu.signOut", "navigation.userMenu.trigger"])
    const revision1 = Object.entries(defaultStrings).filter(([key]) => !additions.has(key))
    expect(revision1).toHaveLength(713)
    expect(createHash('sha256').update(JSON.stringify(revision1)).digest('hex')).toBe('73c7cec0b07f0058d0a48784e76fe8aa86f8e421f85716ff2ea9cc53975c3f7c')
    expect(Object.keys(defaultStrings).slice(713)).toEqual([...additions])
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
