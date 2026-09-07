import { describe, expect, it, vi } from 'vitest'

import { defaultRailLabels, type RailLabels } from '../index'

describe('Rail Labels native behavior', () => {
  it('matches every exact pinned English default', () => {
    expect(defaultRailLabels).toMatchObject({
      lensesHeading: 'Lenses',
      outlineHeading: 'Outline',
      insert: 'Insert',
      locked: 'Locked — inherited from a higher tier',
      unresolvedSource: 'source: unknown',
      empty: 'Add your first field — press ⌘K or the + above.',
      collapseNode: 'Collapse',
      expandNode: 'Expand',
      railRegion: 'Layers',
      exitLens: 'Exit lens (back to Layout)',
    })
  })

  it('formats the six dynamic defaults without normalizing their arguments', () => {
    expect(defaultRailLabels.toggleLens('  أمن  ')).toBe('Toggle   أمن   lens')
    expect(defaultRailLabels.activateLens('<Validation>')).toBe('Show <Validation> lens on the canvas')
    expect(defaultRailLabels.passiveCount(-2)).toBe('+-2')
    expect(defaultRailLabels.source(' tenant ')).toBe(' tenant ')
    expect(defaultRailLabels.viewingLens('Access')).toBe('Viewing: Access')
    expect(defaultRailLabels.lensShortcutHint(0)).toBe('press 0')
  })

  it('allows a host to replace every member without invoking defaults', () => {
    const hostFormatter = vi.fn((value: string) => `host:${value}`)
    const labels: RailLabels = {
      lensesHeading: 'a',
      outlineHeading: 'b',
      insert: 'c',
      toggleLens: hostFormatter,
      activateLens: hostFormatter,
      passiveCount: value => `count:${value}`,
      source: hostFormatter,
      locked: 'd',
      unresolvedSource: 'e',
      empty: 'f',
      collapseNode: 'g',
      expandNode: 'h',
      railRegion: 'i',
      viewingLens: hostFormatter,
      exitLens: 'j',
      lensShortcutHint: value => `shortcut:${value}`,
    }

    expect(labels.toggleLens('lens')).toBe('host:lens')
    expect(hostFormatter).toHaveBeenCalledOnce()
    expect(labels.passiveCount(7)).toBe('count:7')
  })
})
