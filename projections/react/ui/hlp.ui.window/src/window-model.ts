export type WindowModelState = 'default' | 'minimized' | 'maximized'

export interface WindowPosition {
  top: number
  left: number
}

export interface WindowSize {
  width: number
  height: number
}

export type WindowBounds = 'viewport' | { top?: number; left?: number; right?: number; bottom?: number }

export interface ViewportSize {
  width: number
  height: number
}

export interface WindowModel {
  state: WindowModelState
  position: WindowPosition
  size: WindowSize
}

export type WindowModelUpdate =
  | { kind: 'state'; value: WindowModelState }
  | { kind: 'move'; delta: { x: number; y: number } }
  | { kind: 'resize'; edge: 'e' | 's' | 'se'; delta: { x: number; y: number } }

export function dimension(value: number | string, fallback: number): number {
  if (typeof value === 'number') {
    if (!Number.isFinite(value) || value <= 0) throw new Error('invalid-size')
    return value
  }
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

export function validateMinimums(minWidth: number, minHeight: number): void {
  if (!Number.isFinite(minWidth) || minWidth <= 0 || !Number.isFinite(minHeight) || minHeight <= 0) {
    throw new Error('invalid-size')
  }
}

export function validateBounds(bounds: WindowBounds | undefined, minimum: WindowSize): void {
  if (!bounds || bounds === 'viewport') return
  const values = [bounds.top, bounds.left, bounds.right, bounds.bottom].filter(value => value !== undefined)
  if (values.some(value => !Number.isFinite(value))) throw new Error('invalid-bounds')
  if (bounds.left !== undefined && bounds.right !== undefined && bounds.right - bounds.left < minimum.width) {
    throw new Error('invalid-bounds')
  }
  if (bounds.top !== undefined && bounds.bottom !== undefined && bounds.bottom - bounds.top < minimum.height) {
    throw new Error('invalid-bounds')
  }
}

export function initialPosition(top: number | undefined, left: number | undefined, initialTop: number | undefined, initialLeft: number | undefined): WindowPosition {
  return { top: initialTop ?? top ?? 80, left: initialLeft ?? left ?? 80 }
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.max(minimum, Math.min(value, Math.max(minimum, maximum)))
}

export function clampPosition(position: WindowPosition, size: WindowSize, bounds: WindowBounds | undefined, viewport: ViewportSize, titleBarHeight = 40): WindowPosition {
  if (!bounds) return position
  if (bounds === 'viewport') {
    return {
      top: clamp(position.top, 0, viewport.height - titleBarHeight),
      left: clamp(position.left, 0, viewport.width - size.width),
    }
  }
  const minimumTop = bounds.top ?? Number.NEGATIVE_INFINITY
  const minimumLeft = bounds.left ?? Number.NEGATIVE_INFINITY
  const maximumTop = bounds.bottom === undefined ? Number.POSITIVE_INFINITY : bounds.bottom - titleBarHeight
  const maximumLeft = bounds.right === undefined ? Number.POSITIVE_INFINITY : bounds.right - size.width
  return {
    top: clamp(position.top, minimumTop, maximumTop),
    left: clamp(position.left, minimumLeft, maximumLeft),
  }
}

export function movePosition(position: WindowPosition, delta: { x: number; y: number }, size: WindowSize, bounds: WindowBounds | undefined, viewport: ViewportSize): WindowPosition {
  return clampPosition({ top: position.top + delta.y, left: position.left + delta.x }, size, bounds, viewport)
}

export function resizeWindow(size: WindowSize, edge: 'e' | 's' | 'se', delta: { x: number; y: number }, minimum: WindowSize): WindowSize {
  return {
    width: Math.max(minimum.width, edge.includes('e') ? size.width + delta.x : size.width),
    height: Math.max(minimum.height, edge.includes('s') ? size.height + delta.y : size.height),
  }
}

export function applyWindowModelUpdates(initial: WindowModel, updates: readonly WindowModelUpdate[], minimum: WindowSize, bounds: WindowBounds | undefined, viewport: ViewportSize): WindowModel {
  return updates.reduce<WindowModel>((model, update) => {
    if (update.kind === 'state') return { ...model, state: update.value }
    if (update.kind === 'move') return { ...model, position: movePosition(model.position, update.delta, model.size, bounds, viewport) }
    return { ...model, size: resizeWindow(model.size, update.edge, update.delta, minimum) }
  }, initial)
}
