import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createToastService, Toaster, type ToastPosition, type ToastService, type ToastVariant } from '../Toaster'
import { sharedCases } from './fixtures'

// Each neutral case runs the production service and host against its own input and expected
// values. The ids in unasserted concern motion and layout a browser must observe, or the
// cross-projection comparison.
type Case = { id: string; input: Record<string, any>; expected: Record<string, any> }
const cases = sharedCases as unknown as Case[]
const unasserted = ['toaster.rtl', 'toaster.projection-equivalence']

function show(service: ToastService, variant: ToastVariant, message: string, options: { duration?: number | null } = {}) {
  return variant === 'default' ? service.show(message, options) : service[variant](message, options)
}
const toasts = () => Array.from(document.querySelectorAll<HTMLElement>('.hl-toaster__toast'))
const messages = () => toasts().map(toast => toast.querySelector('.hl-toaster__copy > div')!.textContent)

afterEach(() => vi.useRealTimers())

describe('Toaster shared fixture semantics', () => {
  it('covers every case or names it as unasserted', () => {
    for (const entry of cases) expect(entry.id in assertions || unasserted.includes(entry.id), entry.id).toBe(true)
  })
  for (const entry of cases.filter(entry => !unasserted.includes(entry.id))) {
    it(entry.id, async () => { await assertions[entry.id]!(entry) })
  }
})

const assertions: Record<string, (entry: Case) => void | Promise<void>> = {
  'toaster.empty': ({ expected }) => {
    render(<Toaster service={createToastService()}/>)
    expect(document.querySelectorAll('section.hl-toaster').length === 1).toBe(expected.fixedViewport)
    expect(toasts()).toHaveLength(expected.visibleEntries)
    expect(document.querySelectorAll('[role=status],[role=alert]')).toHaveLength(expected.liveItems)
  },
  'toaster.variants': ({ input, expected }) => {
    const service = createToastService({ maximumVisible: 6 })
    render(<Toaster service={service} maximumVisible={6}/>)
    act(() => { for (const variant of input.variants) show(service, variant, `Message ${variant}`) })
    const preserved = JSON.stringify(toasts().map(toast => toast.dataset.variant)) === JSON.stringify(input.variants)
      && JSON.stringify(messages()) === JSON.stringify(input.variants.map((variant: string) => `Message ${variant}`))
    expect(preserved).toBe(expected.allMessagesAndVariantsPreserved)
  },
  'toaster.roles': ({ input, expected }) => {
    const service = createToastService({ maximumVisible: 6 })
    render(<Toaster service={service} maximumVisible={6}/>)
    act(() => { for (const variant of input.variants) show(service, variant, `Message ${variant}`) })
    for (const toast of toasts()) expect(toast.getAttribute('role')).toBe(toast.dataset.variant === 'error' ? expected.errorRole : expected.otherRole)
    expect(document.querySelectorAll('.hl-toaster__toast[aria-live]').length > 0).toBe(expected.explicitAriaLive)
  },
  'toaster.queue-limit': ({ input, expected }) => {
    const service = createToastService()
    render(<Toaster service={service} maximumVisible={input.maximumVisible}/>)
    act(() => { for (const message of input.messages) service.show(message, { duration: 60_000 }) })
    expect(messages()).toEqual(expected.visible)
    expect(service.getSnapshot().queued.map(entry => entry.message)).toEqual(expected.queued)
  },
  'toaster.dismiss-one': ({ input, expected }) => {
    const service = createToastService()
    render(<Toaster service={service} maximumVisible={input.maximumVisible}/>)
    const ids: Record<string, string> = {}
    act(() => { for (const message of input.messages) ids[message] = service.show(message, { duration: 60_000 }) })
    act(() => service.dismiss(ids[input.dismiss]))
    expect(messages()).toEqual(expected.visible)
    expect(input.messages.filter((message: string) => !service.getSnapshot().open.some(entry => entry.message === message))).toEqual(expected.closed)
  },
  'toaster.dismiss-all': ({ input, expected }) => {
    vi.useFakeTimers()
    const service = createToastService()
    for (const message of input.messages) service.show(message, { duration: 200 })
    expect(vi.getTimerCount()).toBe(input.messages.length)
    service.dismiss()
    expect(service.getSnapshot().open).toHaveLength(expected.openEntries)
    expect(vi.getTimerCount() === 0).toBe(expected.timersReleased)
  },
  'toaster.duration-precedence': ({ input, expected }) => {
    const withDefaults = createToastService({ duration: input.host, defaults: { duration: input.defaults } })
    const hostOnly = createToastService({ duration: input.host })
    withDefaults.show('Per call', { duration: input.perCall })
    withDefaults.show('Defaults')
    hostOnly.show('Host')
    expect([...withDefaults.getSnapshot().open, ...hostOnly.getSnapshot().open].map(entry => entry.duration)).toEqual(expected.resolved)
    withDefaults.dispose(); hostOnly.dispose()
  },
  'toaster.error-persistence': ({ input, expected }) => {
    const keep = createToastService({ keepErrorsPersistent: input.keepErrorsPersistent })
    keep.error('Failed', { duration: input.duration })
    expect(keep.getSnapshot().open[0]!.duration === null).toBe(expected.persistent)
    const optOut = createToastService({ keepErrorsPersistent: false })
    optOut.error('Failed', { duration: input.duration })
    expect(optOut.getSnapshot().open[0]!.duration === input.duration).toBe(expected.optOutAllowsFinite)
    keep.dispose(); optOut.dispose()
  },
  'toaster.loading-persistence': ({ input, expected }) => {
    vi.useFakeTimers()
    const service = createToastService()
    const id = service.loading('Saving', { duration: input.duration })
    vi.advanceTimersByTime(input.duration + 300)
    expect(service.getSnapshot().open.length === 1 && service.getSnapshot().open[0]!.duration === null).toBe(expected.persistentUntilTransitionOrDismiss)
    service.dismiss(id)
    expect(service.getSnapshot().open).toHaveLength(0)
  },
  'toaster.close-policy': ({ input, expected }) => {
    const service = createToastService({ maximumVisible: 6 })
    render(<Toaster service={service} showCloseButton={input.showCloseButton} maximumVisible={6}/>)
    act(() => { for (const variant of input.variants) show(service, variant, `Message ${variant}`, { duration: 60_000 }) })
    const hasClose = (variant: string) => document.querySelector(`.hl-toaster__toast[data-variant="${variant}"] .hl-toaster__close`) !== null
    expect(hasClose('default')).toBe(expected.defaultClose)
    expect(hasClose('error')).toBe(expected.errorClose)
  },
  'toaster.description-action': ({ input, expected }) => {
    const onActivate = vi.fn()
    const service = createToastService()
    render(<Toaster service={service} defaults={{ description: input.defaultDescription }}/>)
    act(() => { service.show('Saved', { duration: 60_000, description: input.callDescription, action: { label: input.action, onActivate } }) })
    expect(document.querySelector('.hl-toaster__description')?.textContent).toBe(expected.description)
    fireEvent.click(screen.getByRole('button', { name: input.action }))
    fireEvent.click(screen.getByRole('button', { name: input.action }))
    expect(onActivate).toHaveBeenCalledTimes(expected.actionInvocations)
    expect(service.getSnapshot().open.length === 0).toBe(expected.implicitlyDismissed)
  },
  'toaster.promise-success': async ({ input, expected }) => {
    const service = createToastService()
    let settle!: (value: number) => void
    const tracked = service.trackAsync(new Promise<number>(done => { settle = done }), { loading: 'Saving', success: 'Saved', error: 'Failed' })
    const initial = service.getSnapshot().open[0]!
    expect(initial.variant).toBe(input.initial)
    settle(1)
    await tracked
    const final = service.getSnapshot().open[0]!
    expect(final.id === initial.id).toBe(expected.sameIdentity)
    expect(final.variant).toBe(expected.finalVariant)
    expect(final.duration !== null).toBe(expected.finiteDuration)
    service.dispose()
  },
  'toaster.promise-error': async ({ input, expected }) => {
    const service = createToastService()
    let fail!: (reason: Error) => void
    const tracked = service.trackAsync(new Promise<number>((_, reject) => { fail = reject }), { loading: 'Saving', success: 'Saved', error: 'Failed' })
    const initial = service.getSnapshot().open[0]!
    expect(initial.variant).toBe(input.initial)
    fail(new Error('down'))
    await expect(tracked).rejects.toThrow('down')
    const final = service.getSnapshot().open[0]!
    expect(final.id === initial.id).toBe(expected.sameIdentity)
    expect(final.variant).toBe(expected.finalVariant)
    expect(final.duration === null).toBe(expected.persistent)
    service.dispose()
  },
  'toaster.positions': ({ input, expected }) => {
    const tokens = (input.positions as ToastPosition[]).map(position => {
      const view = render(<Toaster service={createToastService()} position={position}/>)
      const token = view.container.querySelector('section.hl-toaster')!.getAttribute('data-position')
      view.unmount()
      return token
    })
    expect(tokens).toEqual(input.positions)
    expect(new Set(tokens).size === tokens.length).toBe(expected.allDistinct)
    render(<Toaster service={createToastService()}/>)
    expect(document.querySelector('section.hl-toaster')!.getAttribute('data-position')).toBe(expected.default)
  },
  'toaster.localization': ({ input, expected }) => {
    const service = createToastService()
    render(<Toaster service={service} dismissLabel={input.dismissLabel}/>)
    act(() => { service.error('Failed') })
    expect(screen.getByRole('button', { name: expected.closeAccessibleName })).toBeInTheDocument()
  },
  'toaster.invalid-input': ({ input, expected }) => {
    const errors: string[] = []
    const capture = (action: () => void) => { try { action(); errors.push('none') } catch (error) { errors.push((error as Error).message) } }
    const service = createToastService()
    for (const message of input.messages) capture(() => service.show(message))
    for (const limit of input.limits) capture(() => service.configure({ maximumVisible: limit }))
    for (const duration of input.durations) capture(() => service.show('x', { duration: duration === 'NaN' ? Number.NaN : duration }))
    // One code per input, in input order: messages, limits, then durations.
    const [message, limit, duration] = expected.errors
    expect(errors).toEqual([...input.messages.map(() => message), ...input.limits.map(() => limit), ...input.durations.map(() => duration)])
  },
}
