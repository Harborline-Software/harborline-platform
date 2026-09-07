import * as React from 'react'

export type ToastVariant = 'default' | 'success' | 'error' | 'warning' | 'information' | 'loading'
export type ToastPosition = 'top-left' | 'top-center' | 'top-right' | 'bottom-left' | 'bottom-center' | 'bottom-right'

export interface ToastAction {
  label: string
  onActivate: () => void
}

export interface ToastOptions {
  id?: string
  variant?: ToastVariant
  description?: React.ReactNode
  action?: ToastAction
  duration?: number | null
}

export interface ToastDefaults extends Omit<ToastOptions, 'id' | 'variant'> {}

export interface ToastEntry {
  readonly id: string
  readonly message: React.ReactNode
  readonly variant: ToastVariant
  readonly description?: React.ReactNode
  readonly action?: ToastAction
  readonly duration: number | null
}

export interface ToastServiceOptions {
  // number | null for the same reason ToasterProps.duration is: null means no toast expires, and
  // this is what the component forwards to configure(). Ticket 101.
  duration?: number | null
  maximumVisible?: number
  keepErrorsPersistent?: boolean
  defaults?: ToastDefaults
}

export interface ToastSnapshot {
  readonly open: readonly ToastEntry[]
  readonly visible: readonly ToastEntry[]
  readonly queued: readonly ToastEntry[]
}

export interface TrackAsyncMessages<T> {
  loading: React.ReactNode
  success: React.ReactNode | ((value: T) => React.ReactNode)
  error: React.ReactNode | ((reason: unknown) => React.ReactNode)
}

export interface ToastService {
  subscribe(listener: () => void): () => void
  getSnapshot(): ToastSnapshot
  configure(options: ToastServiceOptions): void
  show(message: React.ReactNode, options?: ToastOptions): string
  success(message: React.ReactNode, options?: Omit<ToastOptions, 'variant'>): string
  error(message: React.ReactNode, options?: Omit<ToastOptions, 'variant'>): string
  warning(message: React.ReactNode, options?: Omit<ToastOptions, 'variant'>): string
  information(message: React.ReactNode, options?: Omit<ToastOptions, 'variant'>): string
  loading(message: React.ReactNode, options?: Omit<ToastOptions, 'variant'>): string
  trackAsync<T>(operation: Promise<T> | (() => Promise<T>), messages: TrackAsyncMessages<T>, options?: Omit<ToastOptions, 'variant'>): Promise<T>
  activate(id: string): void
  dismiss(id?: string): void
  clear(): void
  dispose(): void
}

const EMPTY_SNAPSHOT: ToastSnapshot = { open: [], visible: [], queued: [] }

function requireDuration(value: number | null | undefined) {
  if (value !== null && value !== undefined && (!Number.isFinite(value) || value < 0)) throw new Error('invalid-toast-duration')
}

function requireLimit(value: number | undefined) {
  if (value !== undefined && (!Number.isFinite(value) || !Number.isInteger(value) || value <= 0)) throw new Error('invalid-toast-limit')
}

function requireMessage(message: React.ReactNode) {
  if (message === null || message === undefined || (typeof message === 'string' && !message.trim())) throw new Error('toast-message-required')
}

class HostToastService implements ToastService {
  private entries: ToastEntry[] = []
  private listeners = new Set<() => void>()
  private timers = new Map<string, ReturnType<typeof setTimeout>>()
  private actionInvocations = new Set<string>()
  private sequence = 0
  private disposed = false
  private options: Required<Omit<ToastServiceOptions, 'defaults'>> & { defaults: ToastDefaults }
  private snapshot: ToastSnapshot = EMPTY_SNAPSHOT

  constructor(options: ToastServiceOptions) {
    requireDuration(options.duration)
    requireLimit(options.maximumVisible)
    requireDuration(options.defaults?.duration)
    this.options = {
      // `!== undefined`, not `??`: null is a MEANING here (no toast expires) and `??` would
      // collapse it back to the default. Ticket 101.
      duration: options.duration !== undefined ? options.duration : 4000,
      maximumVisible: options.maximumVisible ?? 3,
      keepErrorsPersistent: options.keepErrorsPersistent ?? true,
      defaults: options.defaults ?? {},
    }
  }

  subscribe = (listener: () => void) => {
    this.assertUsable()
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  getSnapshot = () => this.snapshot

  configure(options: ToastServiceOptions) {
    this.assertUsable()
    requireDuration(options.duration)
    requireLimit(options.maximumVisible)
    requireDuration(options.defaults?.duration)
    this.options = {
      duration: options.duration !== undefined ? options.duration : this.options.duration,
      maximumVisible: options.maximumVisible ?? this.options.maximumVisible,
      keepErrorsPersistent: options.keepErrorsPersistent ?? this.options.keepErrorsPersistent,
      defaults: options.defaults ?? this.options.defaults,
    }
    this.publish()
  }

  show = (message: React.ReactNode, options: ToastOptions = {}) => this.upsert(message, options.variant ?? 'default', options)
  success = (message: React.ReactNode, options: Omit<ToastOptions, 'variant'> = {}) => this.upsert(message, 'success', options)
  error = (message: React.ReactNode, options: Omit<ToastOptions, 'variant'> = {}) => this.upsert(message, 'error', options)
  warning = (message: React.ReactNode, options: Omit<ToastOptions, 'variant'> = {}) => this.upsert(message, 'warning', options)
  information = (message: React.ReactNode, options: Omit<ToastOptions, 'variant'> = {}) => this.upsert(message, 'information', options)
  loading = (message: React.ReactNode, options: Omit<ToastOptions, 'variant'> = {}) => this.upsert(message, 'loading', options)

  async trackAsync<T>(operation: Promise<T> | (() => Promise<T>), messages: TrackAsyncMessages<T>, options: Omit<ToastOptions, 'variant'> = {}) {
    const id = this.loading(messages.loading, { ...options, duration: null })
    try {
      const value = await (typeof operation === 'function' ? operation() : operation)
      const message = typeof messages.success === 'function' ? messages.success(value) : messages.success
      this.success(message, { ...options, id })
      return value
    } catch (reason) {
      const message = typeof messages.error === 'function' ? messages.error(reason) : messages.error
      this.error(message, { ...options, id })
      throw reason
    }
  }

  dismiss = (id?: string) => {
    this.assertUsable()
    if (id === undefined) {
      this.clear()
      return
    }
    if (!this.entries.some(entry => entry.id === id)) return
    this.releaseTimer(id)
    this.actionInvocations.delete(id)
    this.entries = this.entries.filter(entry => entry.id !== id)
    this.publish()
  }

  clear = () => {
    this.assertUsable()
    for (const timer of this.timers.values()) clearTimeout(timer)
    this.timers.clear()
    this.actionInvocations.clear()
    this.entries = []
    this.publish()
  }

  activate = (id: string) => {
    const entry = this.entries.find(candidate => candidate.id === id)
    if (!entry?.action || this.actionInvocations.has(entry.id)) return
    this.actionInvocations.add(entry.id)
    entry.action.onActivate()
  }

  dispose = () => {
    if (this.disposed) return
    for (const timer of this.timers.values()) clearTimeout(timer)
    this.timers.clear()
    this.entries = []
    this.snapshot = EMPTY_SNAPSHOT
    this.listeners.clear()
    this.disposed = true
  }

  private upsert(message: React.ReactNode, variant: ToastVariant, options: ToastOptions) {
    this.assertUsable()
    requireMessage(message)
    requireDuration(options.duration)
    const id = options.id ?? `toast-${++this.sequence}`
    const existing = this.entries.findIndex(entry => entry.id === id)
    const duration = this.resolveDuration(variant, options.duration)
    const entry: ToastEntry = {
      id,
      message,
      variant,
      description: options.description ?? this.options.defaults.description,
      action: options.action ?? this.options.defaults.action,
      duration,
    }
    this.releaseTimer(id)
    if (existing >= 0) this.entries = this.entries.map(candidate => candidate.id === id ? entry : candidate)
    else this.entries = [...this.entries, entry]
    this.actionInvocations.delete(id)
    if (duration !== null) this.timers.set(id, setTimeout(() => this.dismiss(id), duration))
    this.publish()
    return id
  }

  private resolveDuration(variant: ToastVariant, duration: number | null | undefined) {
    if (variant === 'loading') return null
    if (variant === 'error' && this.options.keepErrorsPersistent) return null
    const resolved = duration !== undefined ? duration
      : this.options.defaults.duration !== undefined ? this.options.defaults.duration
      : this.options.duration
    requireDuration(resolved)
    return resolved
  }

  private releaseTimer(id: string) {
    const timer = this.timers.get(id)
    if (timer !== undefined) clearTimeout(timer)
    this.timers.delete(id)
  }

  private publish() {
    const split = Math.max(0, this.entries.length - this.options.maximumVisible)
    this.snapshot = { open: this.entries, queued: this.entries.slice(0, split), visible: this.entries.slice(split) }
    for (const listener of this.listeners) listener()
  }

  private assertUsable() {
    if (this.disposed) throw new Error('toast-service-disposed')
  }
}

export function createToastService(options: ToastServiceOptions = {}): ToastService {
  return new HostToastService(options)
}

export interface ToasterProps extends React.HTMLAttributes<HTMLElement> {
  service: ToastService
  position?: ToastPosition
  // number | null, matching ToasterOptions.duration, which this is forwarded to verbatim by
  // service.configure below. Null means no toast expires. It was `number` alone, so the host
  // component could not express what the service already accepted — the same gap Blazor's
  // non-nullable [Parameter] had, in the lane that was supposed to be the reference. Ticket 101.
  duration?: number | null
  maximumVisible?: number
  keepErrorsPersistent?: boolean
  defaults?: ToastDefaults
  showCloseButton?: boolean
  dismissLabel?: string
  direction?: 'ltr' | 'rtl'
}

function ToastIcon({ variant }: { variant: ToastVariant }) {
  if (variant === 'success') return <svg fill="none" focusable="false" viewBox="0 0 16 16"><path d="m4 8 2.5 2.5L12 5" /></svg>
  if (variant === 'error') return <svg fill="none" focusable="false" viewBox="0 0 16 16"><path d="M8 4.25v4.5M8 11.5h.01" /></svg>
  if (variant === 'loading') return <svg className="hl-toaster__loading-icon" fill="none" focusable="false" viewBox="0 0 16 16"><circle cx="8" cy="8" r="5" /><path d="M8 3a5 5 0 0 1 5 5" /></svg>
  return <svg fill="currentColor" focusable="false" viewBox="0 0 16 16"><circle cx="8" cy="8" r="2" /></svg>
}

export function Toaster({
  service, position = 'bottom-right', duration, maximumVisible, keepErrorsPersistent,
  defaults, showCloseButton = false, dismissLabel = 'Dismiss notification', direction,
  className, ...attributes
}: ToasterProps) {
  React.useEffect(() => {
    service.configure({ duration, maximumVisible, keepErrorsPersistent, defaults })
  }, [defaults, duration, keepErrorsPersistent, maximumVisible, service])
  const snapshot = React.useSyncExternalStore(service.subscribe, service.getSnapshot, service.getSnapshot)
  return (
    <section {...attributes} dir={direction} aria-label="Notifications" className={`hl-toaster hl-toaster--${position}${className ? ` ${className}` : ''}`} data-position={position}>
      {snapshot.visible.map(entry => (
        <div key={entry.id} role={entry.variant === 'error' ? 'alert' : 'status'} className="hl-toaster__toast" data-variant={entry.variant} data-toast-id={entry.id}>
          <span aria-hidden="true" className="hl-toaster__marker"><ToastIcon variant={entry.variant} /></span>
          <div className="hl-toaster__copy"><div>{entry.message}</div>{entry.description !== undefined ? <div className="hl-toaster__description">{entry.description}</div> : null}</div>
          {entry.action ? <button type="button" className="hl-toaster__action" onClick={() => service.activate(entry.id)}>{entry.action.label}</button> : null}
          {showCloseButton || entry.variant === 'error' ? <button type="button" className="hl-toaster__close" aria-label={dismissLabel} onClick={() => service.dismiss(entry.id)}><svg aria-hidden="true" fill="none" focusable="false" viewBox="0 0 16 16"><path d="M3 3l10 10M13 3 3 13" /></svg></button> : null}
        </div>
      ))}
    </section>
  )
}
