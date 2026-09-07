import '@testing-library/jest-dom/vitest'

if (globalThis.PointerEvent === undefined) {
  class PointerEventPolyfill extends MouseEvent {
    readonly pointerId: number
    constructor(type: string, init: PointerEventInit = {}) {
      super(type, init)
      this.pointerId = init.pointerId ?? 1
    }
  }
  Object.defineProperty(globalThis, 'PointerEvent', { configurable: true, value: PointerEventPolyfill })
}
