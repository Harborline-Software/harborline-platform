import { vi } from 'vitest'

type ChangeListener = (event: MediaQueryListEvent) => void

export class TestMediaQueryList implements MediaQueryList {
  onchange: ((this: MediaQueryList, ev: MediaQueryListEvent) => unknown) | null = null
  readonly listeners = new Set<ChangeListener>()

  constructor(readonly media: string, public matches: boolean) {}

  addEventListener(type: string, listener: EventListenerOrEventListenerObject): void {
    if (type === 'change' && typeof listener === 'function') {
      this.listeners.add(listener as ChangeListener)
    }
  }

  removeEventListener(type: string, listener: EventListenerOrEventListenerObject): void {
    if (type === 'change' && typeof listener === 'function') {
      this.listeners.delete(listener as ChangeListener)
    }
  }

  addListener(listener: ChangeListener): void { this.listeners.add(listener) }
  removeListener(listener: ChangeListener): void { this.listeners.delete(listener) }
  dispatchEvent(): boolean { return true }

  publish(matches: boolean): void {
    this.matches = matches
    const event = { matches, media: this.media } as MediaQueryListEvent
    this.onchange?.call(this, event)
    for (const listener of this.listeners) listener(event)
  }
}

export class MatchMediaHost {
  readonly entries = new Map<string, TestMediaQueryList>()
  readonly matchMedia = vi.fn((query: string): MediaQueryList => this.get(query))

  seed(query: string, matches: boolean): TestMediaQueryList {
    const entry = new TestMediaQueryList(query, matches)
    this.entries.set(query, entry)
    return entry
  }

  get(query: string): TestMediaQueryList {
    return this.entries.get(query) ?? this.seed(query, false)
  }

  install(): void {
    Object.defineProperty(window, 'matchMedia', { configurable: true, value: this.matchMedia })
  }
}
