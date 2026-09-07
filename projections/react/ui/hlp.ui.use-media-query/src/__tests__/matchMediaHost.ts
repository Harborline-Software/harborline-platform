import { vi } from 'vitest'

type ChangeListener = (event: MediaQueryListEvent) => void

export class TestMediaQueryList implements MediaQueryList {
  onchange: ((this: MediaQueryList, ev: MediaQueryListEvent) => unknown) | null = null
  readonly listeners = new Set<ChangeListener>()
  additions = 0
  removals = 0

  constructor(readonly media: string, public matches: boolean) {}

  addEventListener(type: 'change', listener: ChangeListener): void {
    if (type !== 'change') return
    this.additions += 1
    this.listeners.add(listener)
  }

  removeEventListener(type: 'change', listener: ChangeListener): void {
    if (type !== 'change') return
    this.removals += 1
    this.listeners.delete(listener)
  }

  addListener(listener: ChangeListener): void {
    this.addEventListener('change', listener)
  }

  removeListener(listener: ChangeListener): void {
    this.removeEventListener('change', listener)
  }

  dispatchEvent(): boolean {
    return true
  }

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
    const value = new TestMediaQueryList(query, matches)
    this.entries.set(query, value)
    return value
  }

  get(query: string): TestMediaQueryList {
    return this.entries.get(query) ?? this.seed(query, false)
  }

  install(): void {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: this.matchMedia,
    })
  }
}
