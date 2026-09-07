import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { Window } from '../Window'
import { applyWindowModelUpdates, type WindowModelUpdate } from '../window-model'
import { fixture, performanceCases } from './fixtures'

describe('Window deterministic Tier-C evidence', () => {
  it('window.performance.large-content', () => {
    fixture(performanceCases, 'window.performance.large-content')
    const nodes = Array.from({ length: 256 }, (_, index) => <span data-body-node="true" key={index}>v1-{index}</span>)
    const rendered = render(<Window title="Large">{nodes}</Window>)
    expect(document.querySelectorAll('[data-body-node="true"]')).toHaveLength(256)
    rendered.rerender(<Window title="Large">{nodes.map((_, index) => <span data-body-node="true" key={index}>v2-{index}</span>)}</Window>)
    expect(document.querySelectorAll('[data-body-node="true"]')).toHaveLength(256)
    expect(screen.getByText('v2-255')).toBeInTheDocument()
    expect(screen.queryByText('v1-255')).toBeNull()
  }, 30_000)

  it('window.performance.repeated-update', () => {
    fixture(performanceCases, 'window.performance.repeated-update')
    const updates: WindowModelUpdate[] = Array.from({ length: 32 }, (_, index) => [
      { kind: 'state', value: (['default', 'maximized', 'minimized'] as const)[index % 3] },
      { kind: 'move', delta: { x: 1, y: 2 } },
      { kind: 'resize', edge: 'se', delta: { x: 1, y: 1 } },
    ] as WindowModelUpdate[]).flat()
    expect(updates).toHaveLength(96)
    const latest = applyWindowModelUpdates(
      { state: 'default', position: { top: 80, left: 80 }, size: { width: 400, height: 300 } },
      updates, { width: 200, height: 100 }, 'viewport', { width: 1920, height: 1080 },
    )
    expect(latest).toEqual({ state: 'maximized', position: { top: 144, left: 112 }, size: { width: 432, height: 332 } })

    const onStateChange = vi.fn()
    const onMove = vi.fn()
    const onResize = vi.fn()
    const rendered = render(<Window onMove={onMove} onResize={onResize} onStateChange={onStateChange} state="default" title="Updates" />)
    for (let index = 0; index < 96; index += 1) {
      rendered.rerender(<Window onMove={onMove} onResize={onResize} onStateChange={onStateChange} state={index === 95 ? latest.state : 'default'} title={`Updates ${index}`} />)
    }
    expect(screen.getByRole('dialog')).toHaveAttribute('data-window-state', 'maximized')
    expect(screen.getByRole('dialog')).toHaveAccessibleName('Updates 95')
    expect(onStateChange).not.toHaveBeenCalled()
    expect(onMove).not.toHaveBeenCalled()
    expect(onResize).not.toHaveBeenCalled()
  }, 30_000)
})
