import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GuardedControl, type GuardedControlEvent } from '../GuardedControl'
import { sharedCases } from './fixtures'

// Each neutral case runs the production component against its own input and expected values.
// The ids in unasserted need a real browser (layout, motion, focus deferral that jsdom cannot
// separate from the reused button node) or name no observable renderer outcome.
type Case = { id: string; input: Record<string, any>; expected: Record<string, any> }
const cases = sharedCases as unknown as Case[]
const unasserted = ['guarded-control.localization', 'guarded-control.rtl-motion', 'guarded-control.authorization-boundary', 'guarded-control.projection-equivalence']

function guard(options: { onCommit?: () => void | Promise<void>; label?: string; action?: string; classificationId?: string; disabled?: boolean; describedBy?: readonly string[]; extra?: Record<string, string> } = {}) {
  const events: GuardedControlEvent[] = []
  let commits = 0
  const props = (disabled = options.disabled) => (
    <GuardedControl
      action={options.action ?? 'packages.export'} classificationId={options.classificationId ?? 'new-record-type'} label={options.label ?? 'Sign and save'}
      onCommit={() => { commits++; return options.onCommit?.() }} observe={event => events.push(event)} disabled={disabled} describedBy={options.describedBy} {...options.extra}/>
  )
  const view = render(props())
  const button = () => screen.getByRole('button')
  return {
    view, events, button,
    get commits() { return commits },
    state: () => button().getAttribute('data-guard-state'),
    recoveries: () => events.filter(event => event.type === 'recovered'),
    settlements: () => events.filter(event => event.type === 'commit-settled'),
    states: () => { const transitions = events.filter(event => event.type === 'transition') as Extract<GuardedControlEvent, { type: 'transition' }>[]; return [transitions[0]?.previous, ...transitions.map(event => event.next)] },
    rerender: (disabled: boolean) => view.rerender(props(disabled)),
  }
}

// Browsers move focus off a button that becomes disabled; jsdom keeps it there. Emulate the
// browser while committing so focus restoration after settlement is actually exercised.
function dropFocusFromDisabled(button: HTMLElement) {
  expect(button).toBeDisabled()
  // jsdom ignores blur() on a disabled button, so pass focus through a removed element: the
  // document's focus then falls back to body, as the browser's focus fixup does.
  const sink = document.createElement('div')
  sink.tabIndex = -1
  document.body.append(sink)
  sink.focus()
  sink.remove()
  expect(document.activeElement).toBe(document.body)
}

afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks() })

describe('GuardedControl shared fixture semantics', () => {
  it('covers every case or names it as unasserted', () => {
    const asserted = new Set(Object.keys(assertions))
    for (const entry of cases) expect(asserted.has(entry.id) || unasserted.includes(entry.id), entry.id).toBe(true)
  })
  for (const entry of cases.filter(entry => !unasserted.includes(entry.id))) {
    it(entry.id, async () => { await assertions[entry.id]!(entry) })
  }
})

const assertions: Record<string, (entry: Case) => void | Promise<void>> = {
  'guarded-control.initial-covered': ({ input, expected }) => {
    const h = guard({ label: input.label, disabled: input.disabled })
    expect(h.state()).toBe(expected.state)
    expect(h.commits).toBe(expected.commitCount)
    expect(document.querySelector('[data-guard-state="armed"]') !== null).toBe(expected.armedActionPresent)
  },
  'guarded-control.first-activation-arms': ({ expected }) => {
    const h = guard()
    fireEvent.click(h.button())
    expect(h.state()).toBe(expected.state)
    expect(h.commits).toBe(expected.commitCount)
    expect(screen.getByRole('timer')).toHaveTextContent(`${expected.deadlineMilliseconds / 1000} seconds`)
  },
  'guarded-control.pointer-two-step': async ({ input, expected }) => {
    const h = guard()
    for (const _ of input.activations) fireEvent.click(h.button())
    await act(async () => undefined)
    expect(h.states()).toEqual(expected.states)
    expect(h.commits).toBe(expected.commitCount)
  },
  'guarded-control.keyboard-two-step': async ({ input, expected }) => {
    const user = userEvent.setup()
    const h = guard()
    h.button().focus()
    await user.keyboard(input.keys[0] === 'Enter' ? '{Enter}' : ' ')
    expect(h.commits > 0).toBe(expected.firstActivationCommits)
    await user.keyboard(input.keys[1] === 'Space' ? ' ' : '{Enter}')
    expect(h.commits > 0).toBe(expected.secondActivationCommits)
    expect(h.commits).toBe(expected.commitCount)
  },
  'guarded-control.commit-once': async ({ input, expected }) => {
    let finish!: () => void
    const h = guard({ onCommit: () => new Promise<void>(done => { finish = done }) })
    fireEvent.click(h.button())
    const armed = h.button()
    for (let index = 0; index < input.simultaneousActivations; index++) fireEvent.click(armed)
    expect(h.commits).toBe(expected.commitCount)
    expect(h.states()).toContain('committing')
    await act(async () => finish())
    expect(h.commits).toBe(expected.commitCount)
  },
  'guarded-control.committing-inert': async ({ input, expected }) => {
    vi.useFakeTimers()
    let finish!: () => void
    const h = guard({ onCommit: () => new Promise<void>(done => { finish = done }) })
    fireEvent.click(h.button())
    fireEvent.click(h.button())
    for (const event of input.events) {
      if (event === 'pointer') fireEvent.click(h.button())
      else if (event === 'Enter') fireEvent.keyDown(h.button(), { key: 'Enter' })
      else if (event === 'timeout') act(() => vi.advanceTimersByTime(5000))
      else if (event === 'window-blur') act(() => { window.dispatchEvent(new Event('blur')); vi.advanceTimersByTime(800) })
    }
    expect(h.state()).toBe(expected.state)
    expect(h.commits - 1).toBe(expected.additionalCommitCount)
    expect(h.recoveries()).toHaveLength(expected.recoveryCount)
    await act(async () => finish())
  },
  'guarded-control.commit-completed': async ({ expected }) => {
    let finish!: () => void
    const h = guard({ onCommit: () => new Promise<void>(done => { finish = done }) })
    fireEvent.click(h.button())
    fireEvent.click(h.button())
    dropFocusFromDisabled(h.button())
    await act(async () => finish())
    expect(h.state()).toBe(expected.state)
    expect(h.settlements()).toEqual([{ type: 'commit-settled', outcome: 'completed' }])
    expect(expected.focus).toBe('covered-action')
    expect(document.activeElement).toBe(h.button())
  },
  'guarded-control.commit-rejected': async ({ expected }) => {
    vi.useFakeTimers()
    let fail!: (error: Error) => void
    const h = guard({ onCommit: () => new Promise<void>((_, reject) => { fail = reject }) })
    fireEvent.click(h.button())
    fireEvent.click(h.button())
    dropFocusFromDisabled(h.button())
    await act(async () => fail(new Error('SECRET-RAW-ERROR')))
    expect(h.state()).toBe(expected.stateBeforeExceptionRouting)
    expect(h.settlements()).toEqual([{ type: 'commit-settled', outcome: 'rejected' }])
    expect(document.activeElement).toBe(h.button())
    expect(document.body.innerHTML.includes('SECRET-RAW-ERROR')).toBe(expected.rawErrorRendered)
    // The original failure is routed through the native mechanism only after cleanup.
    expect(() => vi.advanceTimersByTime(1)).toThrow('SECRET-RAW-ERROR')
  },
  'guarded-control.timeout-recovery': ({ input, expected }) => {
    vi.useFakeTimers()
    const h = guard()
    fireEvent.click(h.button())
    act(() => vi.advanceTimersByTime(input.elapsedMilliseconds))
    expect(h.state()).toBe(expected.state)
    expect(h.commits).toBe(expected.commitCount)
    expect(h.recoveries()).toEqual([{ type: 'recovered', reason: expected.recoveryReason }])
    expect(h.recoveries()).toHaveLength(expected.recoveryCount)
  },
  'guarded-control.escape-recovery': async ({ input, expected }) => {
    const h = guard()
    fireEvent.click(h.button())
    fireEvent.keyDown(h.button(), { key: input.key })
    await act(async () => undefined)
    expect(h.state()).toBe(expected.state)
    expect(h.commits).toBe(expected.commitCount)
    expect(h.recoveries()).toEqual([{ type: 'recovered', reason: expected.recoveryReason }])
    expect(document.activeElement).toBe(h.button())
  },
  'guarded-control.host-recovery': ({ input, expected }) => {
    vi.useFakeTimers()
    for (const event of input.events) {
      const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
      const h = guard()
      fireEvent.click(h.button())
      if (event === 'navigation') act(() => { window.dispatchEvent(new PopStateEvent('popstate')) })
      else if (event === 'document-hidden') { visibility.mockReturnValue('hidden'); act(() => { document.dispatchEvent(new Event('visibilitychange')) }) }
      else act(() => { window.dispatchEvent(new Event('blur')); vi.advanceTimersByTime(800) })
      expect(h.state() === 'covered' && h.recoveries().length === 1, event).toBe(expected.eachRecovers)
      expect(h.commits).toBe(expected.commitCount)
      h.view.unmount()
      visibility.mockRestore()
    }
  },
  'guarded-control.recovery-race': ({ expected }) => {
    vi.useFakeTimers()
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
    const h = guard()
    fireEvent.click(h.button())
    act(() => {
      vi.advanceTimersByTime(4999)
      window.dispatchEvent(new Event('blur'))
      visibility.mockReturnValue('hidden')
      document.dispatchEvent(new Event('visibilitychange'))
      vi.advanceTimersByTime(1000)
    })
    expect(h.state()).toBe(expected.state)
    expect(h.recoveries()).toHaveLength(expected.recoveryCount)
    expect(h.commits).toBe(expected.commitCount)
  },
  'guarded-control.disabled': ({ input, expected }) => {
    const h = guard({ disabled: input.disabled, describedBy: input.describedBy })
    fireEvent.click(h.button())
    expect(h.state()).toBe(expected.state)
    expect(h.button().hasAttribute('disabled')).toBe(expected.nativeDisabled)
    expect(h.commits).toBe(expected.commitCount)
    expect(h.button().getAttribute('aria-describedby') === input.describedBy.join(' ')).toBe(expected.descriptionAssociated)
  },
  'guarded-control.became-disabled': ({ input, expected }) => {
    const h = guard()
    fireEvent.click(h.button())
    h.rerender(input.disabled)
    expect(h.state()).toBe(expected.state)
    expect(h.recoveries()).toEqual([{ type: 'recovered', reason: expected.recoveryReason }])
    expect(h.recoveries()).toHaveLength(expected.recoveryCount)
  },
  'guarded-control.focus': async ({ expected }) => {
    const h = guard()
    fireEvent.click(h.button())
    expect(document.activeElement === h.button() && h.state() === 'armed').toBe(expected.focusMovesToArmed)
    fireEvent.keyDown(h.button(), { key: 'Escape' })
    await act(async () => undefined)
    expect(document.activeElement === h.button() && h.state() === 'covered').toBe(expected.focusReturnsAfterEverySettlement)
  },
  'guarded-control.disposal': ({ input, expected }) => {
    vi.useFakeTimers()
    const h = guard()
    fireEvent.click(h.button())
    const before = h.events.length
    const focus = vi.spyOn(HTMLElement.prototype, 'focus')
    h.view.unmount()
    for (const event of input.laterEvents) {
      if (event === 'timeout') act(() => vi.advanceTimersByTime(5000))
      else window.dispatchEvent(new PopStateEvent('popstate'))
    }
    expect(h.events.length - before).toBe(expected.staleEventCount)
    expect(focus.mock.calls.length > 0).toBe(expected.focusAttempted)
    expect(vi.getTimerCount() === 0).toBe(expected.timersDisposed)
  },
  'guarded-control.invalid-input': ({ input, expected }) => {
    for (const variant of input.variants) {
      const h = guard(variant === 'blank-label' ? { label: ' ' } : variant === 'blank-action' ? { action: ' ' } : { classificationId: ' ' })
      fireEvent.click(h.button())
      fireEvent.click(h.button())
      expect(h.button().hasAttribute('disabled')).toBe(expected.inert)
      expect(h.state()).toBe(expected.state)
      expect(h.commits).toBe(expected.commitCount)
      h.view.unmount()
    }
  },
  'guarded-control.host-attributes': ({ input, expected }) => {
    const h = guard({ extra: { 'data-testid': input.attributes['data-testid'] } })
    expect(h.view.container.querySelector('.hl-guarded-control')?.getAttribute('data-testid') === input.attributes['data-testid']).toBe(expected.preserved)
    expect(h.button()).toHaveAttribute('data-guard-state', 'covered')
    expect(h.button()).toHaveAccessibleName(/Sign and save/)
  },
}
