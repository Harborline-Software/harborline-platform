import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { normalizeShortcut, persistenceKey, readRecord, useShellAxis, useShellShortcuts, writeRecord, type ShellStorage } from '../shell-state'

function memoryStorage(seed: Record<string, string> = {}): ShellStorage & { data: Record<string, string> } {
  const data = { ...seed }
  return { data, get: async key => data[key] ?? null, set: async (key, value) => { data[key] = value } }
}
const isBoolean = (v: unknown): v is boolean => typeof v === 'boolean'

describe('app-shell state seam', () => {
  it('builds versioned keys', () => {
    expect(persistenceKey('ops', 'collapsed')).toBe('hlp-app-shell:v1:ops:collapsed')
    expect(persistenceKey('ops', 'switcher', 'tenant', 'pins')).toBe('hlp-app-shell:v1:ops:switcher:tenant:pins')
  })
  it('treats corrupt, wrong-version, and type-invalid records as absent', async () => {
    const storage = memoryStorage({ a: 'not-json', b: '{"v":99,"value":true}', c: '{"v":1,"value":"nope"}', d: '{"v":1,"value":true}' })
    expect(await readRecord(storage, 'a', isBoolean)).toBeNull()
    expect(await readRecord(storage, 'b', isBoolean)).toBeNull()
    expect(await readRecord(storage, 'c', isBoolean)).toBeNull()
    expect(await readRecord(storage, 'd', isBoolean)).toBe(true)
    expect(await readRecord(undefined, 'd', isBoolean)).toBeNull()
  })
  it('paints defaults first then applies one restore update and writes on change', async () => {
    const storage = memoryStorage({ 'hlp-app-shell:v1:ops:collapsed': '{"v":1,"value":true}' })
    const { result } = renderHook(() => useShellAxis<boolean>(undefined, false, persistenceKey('ops', 'collapsed'), storage, isBoolean))
    expect(result.current[0]).toBe(false)
    await waitFor(() => expect(result.current[0]).toBe(true))
    act(() => result.current[1](false))
    expect(result.current[0]).toBe(false)
    await waitFor(() => expect(storage.data['hlp-app-shell:v1:ops:collapsed']).toBe('{"v":1,"value":false}'))
  })
  it('controlled value wins and setter does not mutate local state', async () => {
    const storage = memoryStorage({ k: '{"v":1,"value":true}' })
    const { result } = renderHook(() => useShellAxis<boolean>(false, true, 'k', storage, isBoolean))
    expect(result.current[0]).toBe(false)
    act(() => result.current[1](true))
    expect(result.current[0]).toBe(false)
  })
  it('swallows storage failures', async () => {
    const bad: ShellStorage = { get: async () => { throw new Error('io') }, set: async () => { throw new Error('io') } }
    expect(await readRecord(bad, 'k', isBoolean)).toBeNull()
    expect(() => writeRecord(bad, 'k', true)).not.toThrow()
  })
  it('normalizes shortcuts and guards editable targets', () => {
    expect(normalizeShortcut(new KeyboardEvent('keydown', { key: 'b', ctrlKey: true }))).toBe('Mod+B')
    expect(normalizeShortcut(new KeyboardEvent('keydown', { key: 'k', metaKey: true }))).toBe('Mod+K')
    expect(normalizeShortcut(new KeyboardEvent('keydown', { key: 'b' }))).toBeNull()
    const toggle = vi.fn(); const search = vi.fn()
    renderHook(() => useShellShortcuts({ onToggleNavigation: toggle, onOpenSearch: search }))
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'b', ctrlKey: true, cancelable: true }))
    expect(toggle).toHaveBeenCalledTimes(1)
    const input = document.createElement('input'); document.body.append(input); input.focus()
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'b', ctrlKey: true, bubbles: true, cancelable: true }))
    expect(toggle).toHaveBeenCalledTimes(1)
    input.remove()
    const prevented = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, cancelable: true }); prevented.preventDefault()
    document.dispatchEvent(prevented)
    expect(search).not.toHaveBeenCalled()
  })
  it('null disables one shortcut without the other', () => {
    const toggle = vi.fn(); const search = vi.fn()
    renderHook(() => useShellShortcuts({ openSearch: null, onToggleNavigation: toggle, onOpenSearch: search }))
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, cancelable: true }))
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'b', ctrlKey: true, cancelable: true }))
    expect(search).not.toHaveBeenCalled()
    expect(toggle).toHaveBeenCalledTimes(1)
  })
})
