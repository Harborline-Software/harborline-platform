import * as React from 'react'
import { createPortal } from 'react-dom'

import { cn } from '@harborline-platform/hlp.ui.cn'
import {
  clampPosition,
  dimension,
  initialPosition,
  movePosition,
  resizeWindow,
  validateBounds,
  validateMinimums,
  type WindowPosition,
  type WindowSize,
} from './window-model'

export type WindowState = 'default' | 'minimized' | 'maximized'

export interface WindowActionsBarProps {
  children: React.ReactNode
}

export function WindowActionsBar({ children }: WindowActionsBarProps): React.ReactElement {
  return <div className="hl-window__actions-bar" data-window-actions="true" onPointerDown={event => event.stopPropagation()}>{children}</div>
}

export interface WindowProps {
  title?: React.ReactNode
  children?: React.ReactNode
  width?: number | string
  height?: number | string
  top?: number
  left?: number
  initialTop?: number
  initialLeft?: number
  minWidth?: number
  minHeight?: number
  draggable?: boolean
  resizable?: boolean
  closable?: boolean
  minimizable?: boolean
  maximizable?: boolean
  modal?: boolean
  state?: WindowState
  defaultState?: WindowState
  onClose?: () => void
  onStateChange?: (state: WindowState) => void
  onMove?: (position: { top: number; left: number }) => void
  onResize?: (size: { width: number; height: number }) => void
  dragBounds?: 'viewport' | { top?: number; left?: number; right?: number; bottom?: number }
  autoFocus?: boolean
  doubleClickStageChange?: boolean
  minimizeButton?: React.ComponentType<React.ButtonHTMLAttributes<HTMLButtonElement>>
  maximizeButton?: React.ComponentType<React.ButtonHTMLAttributes<HTMLButtonElement>>
  restoreButton?: React.ComponentType<React.ButtonHTMLAttributes<HTMLButtonElement>>
  overlayStyle?: React.CSSProperties
  className?: string
  labels?: Partial<{
    window: string
    close: string
    minimize: string
    maximize: string
    restore: string
    move: string
    moveDescription: string
    resizeHeight: string
    resizeWidth: string
  }>
}

const focusableSelector = 'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'

function trapModalTab(event: React.KeyboardEvent<HTMLElement>, container: HTMLElement): void {
  if (event.key !== 'Tab') return
  const focusable = Array.from(container.querySelectorAll<HTMLElement>(focusableSelector)).filter(value => value.tabIndex >= 0)
  if (focusable.length === 0) {
    event.preventDefault()
    container.focus()
    return
  }
  const first = focusable[0]
  const last = focusable[focusable.length - 1]
  if (event.shiftKey && (document.activeElement === first || document.activeElement === container)) {
    event.preventDefault(); last.focus()
  } else if (!event.shiftKey && (document.activeElement === last || document.activeElement === container)) {
    event.preventDefault(); first.focus()
  }
}

export function Window({
  title,
  children,
  width = 400,
  height = 300,
  top,
  left,
  initialTop,
  initialLeft,
  minWidth = 200,
  minHeight = 100,
  draggable = true,
  resizable = true,
  closable = true,
  minimizable = true,
  maximizable = true,
  modal = false,
  state: controlledState,
  defaultState = 'default',
  onClose,
  onStateChange,
  onMove,
  onResize,
  dragBounds,
  autoFocus = true,
  doubleClickStageChange = true,
  minimizeButton: MinimizeButton,
  maximizeButton: MaximizeButton,
  restoreButton: RestoreButton,
  overlayStyle,
  className,
  labels,
}: WindowProps): React.ReactPortal {
  validateMinimums(minWidth, minHeight)
  validateBounds(dragBounds, { width: minWidth, height: minHeight })

  const startPosition = React.useMemo(() => initialPosition(top, left, initialTop, initialLeft), [])
  const startSize = React.useMemo(() => ({ width: dimension(width, 400), height: dimension(height, 300) }), [])
  const [uncontrolledState, setUncontrolledState] = React.useState<WindowState>(defaultState)
  const [position, setPosition] = React.useState<WindowPosition>(startPosition)
  const [size, setSize] = React.useState<WindowSize>(startSize)
  const positionRef = React.useRef(position)
  const sizeRef = React.useRef(size)
  const dragRef = React.useRef<{ pointerId: number; startX: number; startY: number; origin: WindowPosition } | null>(null)
  const resizeRef = React.useRef<{ pointerId: number; edge: 'e' | 's' | 'se'; startX: number; startY: number; origin: WindowSize } | null>(null)
  const containerRef = React.useRef<HTMLDivElement>(null)
  const modalActiveRef = React.useRef(false)
  const titleId = React.useId()
  const state = controlledState ?? uncontrolledState
  const minimized = state === 'minimized'
  const maximized = state === 'maximized'
  modalActiveRef.current = modal && !minimized
  const copy = {
    window: labels?.window ?? 'Window', close: labels?.close ?? 'Close', minimize: labels?.minimize ?? 'Minimize',
    maximize: labels?.maximize ?? 'Maximize', restore: labels?.restore ?? 'Restore', move: labels?.move ?? 'Move window',
    moveDescription: labels?.moveDescription ?? 'Press arrow keys to move window',
    resizeHeight: labels?.resizeHeight ?? 'Resize height from bottom edge',
    resizeWidth: labels?.resizeWidth ?? 'Resize width from right edge',
  }

  const viewport = () => ({ width: window.innerWidth, height: window.innerHeight })
  const changeState = (next: WindowState) => {
    if (controlledState === undefined) setUncontrolledState(next)
    onStateChange?.(next)
  }
  const updatePosition = (next: WindowPosition) => {
    positionRef.current = next
    setPosition(next)
    onMove?.(next)
  }
  const updateSize = (next: WindowSize) => {
    const prior = sizeRef.current
    if (next.width === prior.width && next.height === prior.height) return
    sizeRef.current = next
    setSize(next)
    onResize?.(next)
  }
  const clearPointerInteraction = () => {
    dragRef.current = null
    resizeRef.current = null
  }

  React.useLayoutEffect(() => {
    const priorFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const containFocus = (event: FocusEvent) => {
      const target = event.target
      if (modalActiveRef.current && target instanceof Node && containerRef.current && !containerRef.current.contains(target)) {
        containerRef.current.focus()
      }
    }
    if (autoFocus) containerRef.current?.focus()
    document.addEventListener('focusin', containFocus)
    return () => {
      document.removeEventListener('focusin', containFocus)
      priorFocus?.focus()
    }
  }, [])

  const actions = React.Children.toArray(children).find(child => React.isValidElement(child) && child.type === WindowActionsBar)
  const body = React.Children.toArray(children).filter(child => !(React.isValidElement(child) && child.type === WindowActionsBar))

  const startDrag = (event: React.PointerEvent<HTMLElement>) => {
    if (!draggable || state !== 'default') return
    dragRef.current = { pointerId: event.pointerId, startX: event.clientX, startY: event.clientY, origin: positionRef.current }
    event.currentTarget.setPointerCapture?.(event.pointerId)
  }
  const startResize = (edge: 'e' | 's' | 'se') => (event: React.PointerEvent<HTMLElement>) => {
    if (!resizable || state !== 'default') return
    event.stopPropagation()
    resizeRef.current = { pointerId: event.pointerId, edge, startX: event.clientX, startY: event.clientY, origin: sizeRef.current }
    event.currentTarget.setPointerCapture?.(event.pointerId)
  }
  const handlePointerMove = (event: React.PointerEvent<HTMLElement>) => {
    const drag = dragRef.current
    if (drag?.pointerId === event.pointerId) {
      updatePosition(clampPosition({ top: drag.origin.top + event.clientY - drag.startY, left: drag.origin.left + event.clientX - drag.startX }, sizeRef.current, dragBounds, viewport()))
    }
    const resize = resizeRef.current
    if (resize?.pointerId === event.pointerId) {
      updateSize(resizeWindow(resize.origin, resize.edge, { x: event.clientX - resize.startX, y: event.clientY - resize.startY }, { width: minWidth, height: minHeight }))
    }
  }
  const moveByKey = (event: React.KeyboardEvent<HTMLElement>) => {
    if (!draggable || state !== 'default' || !event.key.startsWith('Arrow')) return
    const step = event.shiftKey ? 50 : 10
    const delta = {
      x: event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0,
      y: event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0,
    }
    if (delta.x === 0 && delta.y === 0) return
    event.preventDefault()
    updatePosition(movePosition(positionRef.current, delta, sizeRef.current, dragBounds, viewport()))
  }
  const resizeByKey = (edge: 'e' | 's') => (event: React.KeyboardEvent<HTMLElement>) => {
    if (!resizable || state !== 'default' || !event.key.startsWith('Arrow')) return
    const step = event.shiftKey ? 50 : 10
    const delta = {
      x: edge === 'e' ? (event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0) : 0,
      y: edge === 's' ? (event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0) : 0,
    }
    if (delta.x === 0 && delta.y === 0) return
    event.preventDefault()
    updateSize(resizeWindow(sizeRef.current, edge, delta, { width: minWidth, height: minHeight }))
  }

  const MinimizeControl = MinimizeButton
  const MaximizeControl = maximized ? (RestoreButton ?? MaximizeButton) : MaximizeButton
  const style: React.CSSProperties = maximized
    ? { position: 'fixed', inset: 0, width: '100vw', height: '100vh', zIndex: 1000 }
    : { position: 'fixed', top: position.top, left: position.left, width: size.width, height: minimized ? 'auto' : size.height, zIndex: 1000 }

  return createPortal(<>
    {modal && !minimized ? <div aria-hidden="true" className="hl-window__overlay" data-testid="window-overlay" style={overlayStyle} /> : null}
    <div
      aria-label={title == null ? copy.window : undefined}
      aria-labelledby={title != null ? titleId : undefined}
      aria-modal={modal ? 'true' : undefined}
      className={cn('hl-window', maximized && 'hl-window--maximized', minimized && 'hl-window--minimized', className)}
      data-hl-module="hlp.ui.window"
      data-window-state={state}
      onKeyDown={event => {
        if (event.key === 'Escape') {
          event.stopPropagation()
          if (maximized) changeState('default')
          else if (closable) onClose?.()
          return
        }
        if (modal && containerRef.current) trapModalTab(event, containerRef.current)
      }}
      onLostPointerCapture={clearPointerInteraction}
      onPointerCancel={clearPointerInteraction}
      onPointerMove={handlePointerMove}
      onPointerUp={clearPointerInteraction}
      ref={containerRef}
      role="dialog"
      style={style}
      tabIndex={-1}
    >
      <div
        aria-description={draggable ? copy.moveDescription : undefined}
        aria-label={draggable ? copy.move : undefined}
        className={cn('hl-window__title-bar', draggable && state === 'default' && 'hl-window__title-bar--draggable')}
        data-testid="window-title-bar"
        onDoubleClick={event => {
          if (!doubleClickStageChange || minimized || (event.target as HTMLElement).closest('[data-window-actions="true"]')) return
          changeState(maximized ? 'default' : 'maximized')
        }}
        onKeyDown={moveByKey}
        onPointerDown={startDrag}
        role={draggable ? 'group' : undefined}
        tabIndex={draggable ? 0 : undefined}
      >
        <span className="hl-window__title" id={titleId}>{title}</span>
        <div className="hl-window__actions" data-window-actions="true" onPointerDown={event => event.stopPropagation()}>
          {actions}
          {minimizable ? MinimizeControl
            ? <MinimizeControl aria-label={minimized ? copy.restore : copy.minimize} onClick={() => changeState(minimized ? 'default' : 'minimized')} />
            : <button aria-label={minimized ? copy.restore : copy.minimize} className="hl-window__control" onClick={() => changeState(minimized ? 'default' : 'minimized')} type="button"><svg aria-hidden="true" viewBox="0 0 20 20"><path d="M5 14.5h10" /></svg></button>
            : null}
          {maximizable ? MaximizeControl
            ? <MaximizeControl aria-label={maximized ? copy.restore : copy.maximize} onClick={() => changeState(maximized ? 'default' : 'maximized')} />
            : <button aria-label={maximized ? copy.restore : copy.maximize} className="hl-window__control" onClick={() => changeState(maximized ? 'default' : 'maximized')} type="button"><svg aria-hidden="true" viewBox="0 0 20 20">{maximized ? <><rect x="4" y="6" width="10" height="9" rx="1" /><path d="M7 6V4h9v9h-2" /></> : <rect x="4" y="4" width="12" height="12" rx="1" />}</svg></button>
            : null}
          {closable ? <button aria-label={copy.close} className="hl-window__control hl-window__control--close" onClick={onClose} type="button"><svg aria-hidden="true" viewBox="0 0 20 20"><path d="m5 5 10 10M15 5 5 15" /></svg></button> : null}
        </div>
      </div>
      {!minimized ? <div className="hl-window__body">{body}</div> : null}
      {resizable && state === 'default' && !minimized ? <>
        <div aria-hidden="true" className="hl-window__resize hl-window__resize--corner" data-testid="window-resize-corner" onPointerDown={startResize('se')} />
        <div aria-label={copy.resizeHeight} aria-orientation="horizontal" aria-valuemax={10000} aria-valuemin={minHeight} aria-valuenow={size.height} className="hl-window__resize hl-window__resize--height" data-testid="window-resize-height" onKeyDown={resizeByKey('s')} onPointerDown={startResize('s')} role="separator" tabIndex={0} />
        <div aria-label={copy.resizeWidth} aria-orientation="vertical" aria-valuemax={10000} aria-valuemin={minWidth} aria-valuenow={size.width} className="hl-window__resize hl-window__resize--width" data-testid="window-resize-width" onKeyDown={resizeByKey('e')} onPointerDown={startResize('e')} role="separator" tabIndex={0} />
      </> : null}
    </div>
  </>, document.body)
}
