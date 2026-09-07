import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { createLayersState, reduceLayersState, useLayers } from '../index'

describe('Layers State native behavior', () => {
  it('never mutates a prior snapshot or enabled-id set', () => {
    const initial = createLayersState(['layout'])
    const activated = reduceLayersState(initial, { type: 'activate', id: 'rules' })
    const toggled = reduceLayersState(activated, { type: 'toggle', id: 'layout' })

    expect([...initial.enabledIds]).toEqual(['layout'])
    expect([...activated.enabledIds]).toEqual(['layout', 'rules'])
    expect([...toggled.enabledIds]).toEqual(['rules'])
    expect(initial).not.toBe(activated)
    expect(activated).not.toBe(toggled)
  })

  it('returns the same outcome for already-satisfied activate and clear commands', () => {
    const active = createLayersState(['layout'])
    expect(reduceLayersState(active, { type: 'activate', id: 'layout' })).toBe(active)
    const neutral = reduceLayersState(active, { type: 'clear' })
    expect(reduceLayersState(neutral, { type: 'clear' })).toBe(neutral)
  })

  it('projects the pure transitions through the compatibility hook', () => {
    const lenses = [{ id: 'layout' }, { id: 'rules' }]
    const { result } = renderHook(() => useLayers(lenses))

    expect(result.current.activeId).toBe('layout')
    act(() => result.current.setActive('rules'))
    expect(result.current.isActive('rules')).toBe(true)
    expect(result.current.isEnabled('layout')).toBe(true)
    act(() => result.current.toggleEnabled('rules'))
    expect(result.current.activeId).toBeNull()
    expect([...result.current.enabledIds]).toEqual(['layout'])
  })

  it('does not reconcile state when the host lens list changes', () => {
    const { result, rerender } = renderHook(
      ({ lenses }) => useLayers(lenses),
      { initialProps: { lenses: [{ id: 'layout' }] } },
    )
    rerender({ lenses: [{ id: 'replacement' }] })
    expect(result.current.activeId).toBe('layout')
  })
})
