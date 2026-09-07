import { fireEvent, render } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { EmptyState } from '../EmptyState'

const maximumFixtureTextLength = 4_096
const updateCycles = 96
const maximumDescendantElements = 7

function fixtureText(prefix: string): string {
  return prefix.padEnd(maximumFixtureTextLength, 'x').slice(0, maximumFixtureTextLength)
}

describe('EmptyState Tier-C performance proof', () => {
  it('reuses a bounded DOM and listener surface across maximum-size state changes', () => {
    const callbacks = Array.from({ length: updateCycles }, () => vi.fn())
    const rendered = render(
      <EmptyState
        variant="positive"
        title={fixtureText('title-0')}
        description={fixtureText('description-0')}
        action={{ label: 'Continue', onClick: callbacks[0] }}
      />,
    )

    const root = rendered.container.querySelector('.hl-empty-state')
    const icon = rendered.container.querySelector('.hl-empty-state__icon')
    const title = rendered.container.querySelector('.hl-empty-state__title')
    const description = rendered.container.querySelector('.hl-empty-state__description')
    const action = rendered.container.querySelector('.hl-empty-state__action')

    expect(root).not.toBeNull()
    expect(icon).not.toBeNull()
    expect(title).not.toBeNull()
    expect(description).not.toBeNull()
    expect(action).not.toBeNull()

    const createElement = vi.spyOn(document, 'createElement')
    const createElementNs = vi.spyOn(document, 'createElementNS')
    const addEventListener = vi.spyOn(EventTarget.prototype, 'addEventListener')

    try {
      for (let cycle = 0; cycle < updateCycles; cycle += 1) {
        // These variants have isomorphic SVG trees, so allocations indicate
        // reconciliation churn rather than an intentional structure change.
        const variant = cycle % 2 === 0 ? 'positive' : 'actionable'
        rendered.rerender(
          <EmptyState
            variant={variant}
            title={fixtureText(`title-${cycle}`)}
            description={fixtureText(`description-${cycle}`)}
            action={{ label: 'Continue', onClick: callbacks[cycle] }}
          />,
        )

        expect(rendered.container.querySelector('.hl-empty-state')).toBe(root)
        expect(rendered.container.querySelector('.hl-empty-state__icon')).toBe(icon)
        expect(rendered.container.querySelector('.hl-empty-state__title')).toBe(title)
        expect(rendered.container.querySelector('.hl-empty-state__description')).toBe(description)
        expect(rendered.container.querySelector('.hl-empty-state__action')).toBe(action)
        expect(root?.querySelectorAll('*').length).toBeLessThanOrEqual(maximumDescendantElements)
      }

      expect(createElement).not.toHaveBeenCalled()
      expect(createElementNs).not.toHaveBeenCalled()
      expect(addEventListener).not.toHaveBeenCalled()

      fireEvent.click(action as HTMLButtonElement)
      expect(callbacks.slice(0, -1).every(callback => callback.mock.calls.length === 0)).toBe(true)
      expect(callbacks.at(-1)).toHaveBeenCalledTimes(1)
      expect(root).not.toHaveAttribute('role')
      expect(root).not.toHaveAttribute('aria-live')
    } finally {
      createElement.mockRestore()
      createElementNs.mockRestore()
      addEventListener.mockRestore()
    }
  })
})
