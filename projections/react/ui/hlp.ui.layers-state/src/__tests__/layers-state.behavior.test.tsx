import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { createLayersState, reduceLayersState, useLayers } from '../index'

const lenses = [{ id: 'layout' }, { id: 'rules' }]

describe('Layers State literal transition behavior', () => {
  it('reactivates a passively enabled layer without disabling the previous active layer', () => {
    const initial = createLayersState(['layout', 'rules'])
    const rulesActive = reduceLayersState(initial, { type: 'activate', id: 'rules' })
    const layoutActive = reduceLayersState(rulesActive, { type: 'activate', id: 'layout' })

    // Oracle: interface invariants 2-4; activation replaces active status and retains enabled IDs.
    expect(layoutActive.activeId).toBe('layout')
    expect(layoutActive.enabledIds).toEqual(new Set(['layout', 'rules']))
    expect(rulesActive.activeId).toBe('rules')
    expect(rulesActive.enabledIds).toEqual(new Set(['layout', 'rules']))
    expect(initial.activeId).toBe('layout')
    expect(initial.enabledIds).toEqual(new Set(['layout']))
  })

  it('retains the active layer when a different passively enabled layer is toggled off', () => {
    const initial = createLayersState(['layout'])
    const rulesActive = reduceLayersState(initial, { type: 'activate', id: 'rules' })
    const layoutDisabled = reduceLayersState(rulesActive, { type: 'toggle', id: 'layout' })

    // Oracle: invariant 6; only toggling the active ID off clears active status.
    expect(layoutDisabled.activeId).toBe('rules')
    expect(layoutDisabled.enabledIds).toEqual(new Set(['rules']))
    expect(rulesActive.activeId).toBe('rules')
    expect(rulesActive.enabledIds).toEqual(new Set(['layout', 'rules']))
  })

  it('clears active through the hook while retaining every enabled layer', () => {
    const { result } = renderHook(() => useLayers(lenses))
    act(() => result.current.setActive('rules'))
    act(() => result.current.clearActive())

    // Oracle: layers.clear-retains-enabled and projection invariant 9, not a reducer-derived expectation.
    expect(result.current.activeId).toBeNull()
    expect(result.current.enabledIds).toEqual(new Set(['layout', 'rules']))
    expect(result.current.isActive('layout')).toBe(false)
    expect(result.current.isActive('rules')).toBe(false)
    expect(result.current.isEnabled('layout')).toBe(true)
    expect(result.current.isEnabled('rules')).toBe(true)

    act(() => result.current.clearActive())
    expect(result.current.activeId).toBeNull()
    expect(result.current.enabledIds).toEqual(new Set(['layout', 'rules']))
  })

  it('answers enabled and active queries from the latest hook state', () => {
    const { result } = renderHook(() => useLayers(lenses))
    expect(result.current.isEnabled('rules')).toBe(false)
    expect(result.current.isActive('rules')).toBe(false)

    act(() => result.current.setActive('rules'))
    expect(result.current.isEnabled('rules')).toBe(true)
    expect(result.current.isActive('rules')).toBe(true)
    expect(result.current.isActive('layout')).toBe(false)

    act(() => result.current.toggleEnabled('rules'))
    expect(result.current.isEnabled('rules')).toBe(false)
    expect(result.current.isActive('rules')).toBe(false)
    expect(result.current.isEnabled('layout')).toBe(true)
    expect(result.current.isActive('layout')).toBe(false)
    expect(result.current.activeId).toBeNull()
    expect(result.current.enabledIds).toEqual(new Set(['layout']))
  })
})
