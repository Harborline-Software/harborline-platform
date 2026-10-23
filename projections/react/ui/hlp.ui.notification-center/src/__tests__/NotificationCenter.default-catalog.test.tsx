import { fireEvent, render } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { NotificationCenter } from '../NotificationCenter'

// Proves the default copy is read from the default catalog, not an English literal: the catalog's value
// is replaced here, and the component must show the replacement with no provider and no caller text.
vi.mock('@harborline-platform/hlp.ui.default-strings', async original => {
  const actual = await original<typeof import('@harborline-platform/hlp.ui.default-strings')>()
  return { ...actual, defaultStrings: { ...actual.defaultStrings, 'navigation.notifications.title': 'Catalog notifications', 'navigation.notifications.groups': 'Catalog groups' } }
})

describe('NotificationCenter default copy comes from the default catalog', () => {
  it('shows the default catalog value with no provider', () => {
    render(<NotificationCenter items={[{ id: 'p1', title: 'A', timestamp: 'Now', kind: 'proposal' }, { id: 'r1', title: 'B', timestamp: 'Now', kind: 'result' }]}/>)
    fireEvent.click(document.querySelector('.hl-notification-center__trigger')!)
    expect(document.querySelector('[role="dialog"]')!.getAttribute('aria-label')).toBe('Catalog notifications')
    expect(document.querySelector('[role="tablist"]')!.getAttribute('aria-label')).toBe('Catalog groups')
  })
})
