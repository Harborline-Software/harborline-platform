import { describe, expect, it } from 'vitest'
import { createSideNavGroup } from '../index'

describe('SideNavGroup revision-1', () => {
  it('preserves optional labels, ordered items, and nested values', () => {
    const group = createSideNavGroup('main', [{ id: 'home', label: 'Home' }, { id: 'reports', label: 'Reports', children: [{ id: 'daily', label: 'Daily' }] }])
    expect(group.label).toBeUndefined()
    expect(group.items.map(item => item.id)).toEqual(['home', 'reports'])
    expect(group.items[1]?.children?.[0]?.id).toBe('daily')
  })
  it('rejects blank identities', () => expect(() => createSideNavGroup(' ', [])).toThrow('invalid-side-nav-identity'))
})
