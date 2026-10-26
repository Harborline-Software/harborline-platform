import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider, type HarborlineNumberFormatter } from '@harborline-platform/hlp.ui.locale-provider'
import { GuardedControl } from '../GuardedControl'
import { sharedCases } from './fixtures'

// Revision 2 shared cases run the production component against the fixture's own input and expected
// values, as the Blazor lane's AssertCase does, so both lanes are held to the same names and
// descriptions. `disabled` is asserted too, because revision 2 changes what its describedBy carries.
// The other revision-1 behaviour cases are asserted by name in the native suite, not per case here:
// making them case-addressable is the follow-up the shared runner records (ticket 081).
// projection-equivalence compares lanes. An unknown case id fails.
type Case = { id: string; input: Record<string, any>; expected: Record<string, any> }
const cases = sharedCases as unknown as Case[]
const unasserted = [
  'guarded-control.initial-covered', 'guarded-control.first-activation-arms', 'guarded-control.pointer-two-step',
  'guarded-control.keyboard-two-step', 'guarded-control.commit-once', 'guarded-control.committing-inert',
  'guarded-control.commit-completed', 'guarded-control.commit-rejected', 'guarded-control.timeout-recovery',
  'guarded-control.escape-recovery', 'guarded-control.host-recovery', 'guarded-control.recovery-race',
  'guarded-control.became-disabled', 'guarded-control.focus', 'guarded-control.disposal',
  'guarded-control.authorization-boundary', 'guarded-control.localization', 'guarded-control.rtl-motion',
  'guarded-control.host-attributes', 'guarded-control.invalid-input', 'guarded-control.projection-equivalence',
]

afterEach(() => vi.useRealTimers())

describe('GuardedControl shared fixture semantics', () => {
  it('covers every case or names it as unasserted', () => {
    expect(cases.length).toBeGreaterThan(0)
    for (const entry of cases) expect(entry.id in assertions || unasserted.includes(entry.id), entry.id).toBe(true)
    for (const id of [...Object.keys(assertions), ...unasserted]) expect(cases.some(entry => entry.id === id), id).toBe(true)
  })
  for (const entry of cases.filter(entry => !unasserted.includes(entry.id))) {
    it(entry.id, async () => { await assertions[entry.id]!(entry) })
  }
})

const action = () => screen.getByRole('button')
const instructionElement = () => document.querySelector('.hl-guarded-control__instruction')

// One activation arms; a second, with a commit that never settles, holds the committing state.
function reach(state: string) {
  if (state === 'covered') return
  fireEvent.click(action())
  if (state === 'committing') fireEvent.click(action())
}

function renderVariant(variant: Record<string, any>, onCommit: () => void | Promise<void> = () => new Promise<void>(() => undefined)) {
  return render(
    <HarborlineLocaleProvider locale={variant.locale} catalog={variant.catalog} numberFormatter={variant.numberingSystem ? hostDigits(variant.numberingSystem) : undefined}>
      <GuardedControl action="a" classificationId="c" label={variant.label} onCommit={onCommit}/>
    </HarborlineLocaleProvider>,
  )
}

// A host formatter that writes the digits of one CLDR numbering system, as the Blazor lane's is.
function hostDigits(numberingSystem: string): HarborlineNumberFormatter {
  return (value, { locale, options }) => new Intl.NumberFormat(locale, { ...options, numberingSystem }).format(value)
}

const assertions: Record<string, (entry: Case) => void | Promise<void>> = {
  'guarded-control.disabled': ({ input, expected }) => {
    const commit = vi.fn()
    render(<><p id="guard-denial">Complete the affirmation first.</p><GuardedControl action="a" classificationId="c" label="Delete" disabled={input.disabled} describedBy={input.describedBy} onCommit={commit}/></>)
    fireEvent.click(action())
    expect(action().hasAttribute('disabled')).toBe(expected.nativeDisabled)
    expect(action()).toHaveAttribute('data-guard-state', expected.state)
    expect(commit).toHaveBeenCalledTimes(expected.commitCount)
    const ids = action().getAttribute('aria-describedby')?.split(' ') ?? []
    expect(input.describedBy.every((id: string) => ids.includes(id))).toBe(expected.descriptionAssociated)
  },
  'guarded-control.name-is-label': ({ input, expected }) => {
    ;(input.states as string[]).forEach((state, index) => {
      const view = render(<GuardedControl action="a" classificationId="c" label={input.label} onCommit={() => new Promise<void>(() => undefined)}/>)
      reach(state)
      expect(action()).toHaveAttribute('data-guard-state', state)
      expect(action()).toHaveAccessibleName(expected.names[index])
      // The countdown is never part of the name, whatever second it shows.
      const timer = screen.queryByRole('timer')
      if (timer) {
        const countdown = new RegExp((timer.textContent ?? '').replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
        if (expected.timerInName) expect(action()).toHaveAccessibleName(countdown)
        else expect(action()).not.toHaveAccessibleName(countdown)
      }
      view.unmount()
    })
  },
  'guarded-control.description-instruction': ({ input, expected }) => {
    ;(input.states as string[]).forEach((state, index) => {
      const view = render(<><p id={input.describedBy.id}>{input.describedBy.text}</p><GuardedControl action="a" classificationId="c" label={input.label} describedBy={input.describedBy.id} onCommit={() => new Promise<void>(() => undefined)}/></>)
      reach(state)
      expect(action()).toHaveAccessibleDescription(expected.descriptions[index])
      const ids = action().getAttribute('aria-describedby')?.split(' ') ?? []
      const instruction = instructionElement()
      expect(ids.map(id => id === input.describedBy.id ? 'caller' : id === instruction?.id ? 'instruction' : id)).toEqual(expected.describedByOrder)
      expect(instruction?.textContent).toBe(expected.instructions[index])
      expect(instruction !== null && instruction.closest('[aria-hidden="true"], [hidden], .hl-visually-hidden') === null).toBe(expected.instructionVisible)
      view.unmount()
    })
  },
  'guarded-control.description-stable': ({ input, expected }) => {
    vi.useFakeTimers()
    render(<GuardedControl action="a" classificationId="c" label={input.label} onCommit={() => undefined}/>)
    const statuses: string[] = []
    const status = () => screen.getByRole('status').textContent ?? ''
    fireEvent.click(action())
    let elapsed = 0
    ;(input.elapsedSeconds as number[]).forEach((second, index) => {
      act(() => vi.advanceTimersByTime((second - elapsed) * 1000))
      elapsed = second
      statuses.push(status())
      expect(action()).toHaveAccessibleName(expected.names[index])
      expect(action()).toHaveAccessibleDescription(expected.descriptions[index])
      expect(screen.getByRole('timer').textContent).toBe(expected.timers[index])
    })
    act(() => vi.advanceTimersByTime((5 - elapsed) * 1000))
    statuses.push(status())
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(action()).toHaveAccessibleDescription(expected.afterWindow.description)
    expect(status()).toBe(expected.afterWindow.status)
    expect(statuses.filter((value, index) => index === 0 || value !== statuses[index - 1])).toEqual(expected.statusSequence)
  },
  // Every variant asserts the name and the covered and committing descriptions. A description the
  // variant leaves out must be named in knownLimitations with its ticket, or the case fails: the armed
  // description in Arabic and the pseudo-locale is T-1105's deferred duration-localization parity.
  'guarded-control.naming-localization': ({ input, expected }) => {
    ;(input.variants as Array<Record<string, any>>).forEach((variant, index) => {
      const want = expected.variants[index]
      const limited = (assertion: string) => ((want.knownLimitations ?? []) as Array<{ assertion: string; ticket: string }>)
        .some(limitation => limitation.assertion === assertion && limitation.ticket === 'T-1105')
      for (const state of ['covered', 'armed', 'committing']) {
        const view = renderVariant(variant)
        reach(state)
        const context = `${variant.name} ${state}`
        expect(action().closest('[dir]')?.getAttribute('dir'), context).toBe(want.direction)
        expect(action(), context).toHaveAccessibleName(want.names[state])
        if (state in want.descriptions) {
          expect(action(), context).toHaveAccessibleDescription(want.descriptions[state])
          expect(instructionElement()?.textContent, context).toBe(want.descriptions[state])
        } else {
          expect(limited(`descriptions.${state}`), `${context}: description neither asserted nor a named T-1105 limitation`).toBe(true)
        }
        view.unmount()
      }
    })
  },
}
