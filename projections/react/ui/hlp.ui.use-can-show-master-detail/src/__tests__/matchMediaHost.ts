import { vi } from 'vitest'

type Listener = (event: MediaQueryListEvent) => void

export class MatchMediaHost {
  readonly queries: string[] = []

  install(values: Readonly<Record<string, boolean>> = {}): void {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn((query: string): MediaQueryList => {
        this.queries.push(query)
        const listeners = new Set<Listener>()
        return {
          get matches() { return values[query] ?? false },
          media: query,
          onchange: null,
          addListener: () => undefined,
          removeListener: () => undefined,
          addEventListener: (_type: string, listener: EventListenerOrEventListenerObject) => {
            listeners.add(listener as Listener)
          },
          removeEventListener: (_type: string, listener: EventListenerOrEventListenerObject) => {
            listeners.delete(listener as Listener)
          },
          dispatchEvent: () => false,
        }
      }),
    })
  }
}
