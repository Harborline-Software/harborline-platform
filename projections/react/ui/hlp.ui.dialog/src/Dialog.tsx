import * as React from 'react'
import { createPortal } from 'react-dom'

import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'
import {
  handleScrollAffordanceKeyDown,
  useScrollAffordance,
} from '@harborline-platform/hlp.ui.use-scroll-affordance'

export interface DialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  children: React.ReactNode
  footer?: React.ReactNode
  closeOnOverlayClick?: boolean
  closeOnEscape?: boolean
  closeIcon?: boolean
  theme?: 'light' | 'dark'
}

function focusableElements(root: HTMLElement): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>([
    'a[href]',
    'button:not([disabled])',
    'input:not([disabled]):not([type="hidden"])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])',
    '[contenteditable="true"]',
  ].join(','))).filter(element => !element.closest('[hidden], [inert]'))
}

function DialogBody({ children }: { readonly children: React.ReactNode }) {
  const bodyRef = React.useRef<HTMLDivElement | null>(null)
  const scroll = useScrollAffordance(bodyRef, { orientation: 'vertical' })

  return (
    <>
      <div
        className="hl-dialog__body"
        data-dialog-body=""
        onKeyDown={event => handleScrollAffordanceKeyDown(event, event.currentTarget, 'vertical')}
        ref={bodyRef}
        style={{
          maskImage: scroll.maskImage,
          WebkitMaskImage: scroll.maskImage,
        }}
        tabIndex={0}
      >
        {children}
      </div>
      <div aria-atomic="true" aria-live="polite" className="hl-dialog__status">
        {scroll.srMessage}
      </div>
    </>
  )
}

export function Dialog({
  children,
  closeIcon = true,
  closeOnEscape = true,
  closeOnOverlayClick = true,
  description,
  footer,
  onOpenChange,
  open,
  theme,
  title,
}: DialogProps) {
  const { direction, t } = useHarborlineStrings()
  const generatedId = React.useId().replace(/:/g, '')
  const titleId = `hl-dialog-${generatedId}-title`
  const descriptionId = description ? `hl-dialog-${generatedId}-description` : undefined
  const portalRef = React.useRef<HTMLDivElement | null>(null)
  const dialogRef = React.useRef<HTMLDivElement | null>(null)
  const restoreFocusRef = React.useRef<HTMLElement | null>(null)

  React.useLayoutEffect(() => {
    if (!open) return
    const active = document.activeElement
    restoreFocusRef.current = active instanceof HTMLElement ? active : null
    const dialog = dialogRef.current
    if (!dialog) return
    ;(focusableElements(dialog)[0] ?? dialog).focus()

    return () => {
      const restoreTarget = restoreFocusRef.current
      restoreFocusRef.current = null
      if (restoreTarget?.isConnected) restoreTarget.focus()
    }
  }, [open])

  React.useLayoutEffect(() => {
    if (!open || !portalRef.current) return
    const portal = portalRef.current
    const siblings = Array.from(document.body.children).filter(element => element !== portal)
    const inertState = siblings.map(element => (element as HTMLElement).inert)
    const bodyOverflow = document.body.style.overflow
    siblings.forEach(element => { (element as HTMLElement).inert = true })
    document.body.style.overflow = 'hidden'

    return () => {
      siblings.forEach((element, index) => { (element as HTMLElement).inert = inertState[index] ?? false })
      document.body.style.overflow = bodyOverflow
    }
  }, [open])

  React.useEffect(() => {
    if (!open) return
    const onDocumentKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented) return
      if (event.key === 'Escape') {
        if (!closeOnEscape) return
        event.preventDefault()
        event.stopPropagation()
        onOpenChange(false)
        return
      }
      if (event.key !== 'Tab' || !dialogRef.current) return
      const dialog = dialogRef.current
      const focusables = focusableElements(dialog)
      if (focusables.length === 0) {
        event.preventDefault()
        dialog.focus()
        return
      }
      const first = focusables[0]!
      const last = focusables.at(-1)!
      const active = document.activeElement
      if (event.shiftKey && (active === first || !dialog.contains(active))) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && (active === last || !dialog.contains(active))) {
        event.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', onDocumentKeyDown, true)
    return () => document.removeEventListener('keydown', onDocumentKeyDown, true)
  }, [closeOnEscape, onOpenChange, open])

  if (!open || typeof document === 'undefined') return null

  return createPortal(
    <div className="hl-dialog__portal" data-theme={theme} ref={portalRef}>
      <div
        aria-hidden="true"
        className="hl-dialog__overlay"
        data-testid="dialog-overlay"
        onPointerDown={event => {
          if (closeOnOverlayClick && event.currentTarget === event.target) onOpenChange(false)
        }}
      />
      <div
        aria-describedby={descriptionId}
        aria-labelledby={titleId}
        aria-modal="true"
        className="hl-dialog__surface"
        dir={direction}
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <header className="hl-dialog__header">
          <div className="hl-dialog__heading">
            <h2 className="hl-dialog__title" id={titleId}>{title}</h2>
            {description ? <p className="hl-dialog__description" id={descriptionId}>{description}</p> : null}
          </div>
          {closeIcon ? (
            <button
              aria-label={t('common.close')}
              className="hl-dialog__close"
              onClick={() => onOpenChange(false)}
              type="button"
            >
              <svg aria-hidden="true" viewBox="0 0 16 16">
                <path d="M3 3l10 10M13 3L3 13" />
              </svg>
            </button>
          ) : null}
        </header>
        <DialogBody>{children}</DialogBody>
        {footer ? <footer className="hl-dialog__footer">{footer}</footer> : null}
      </div>
    </div>,
    document.body,
  )
}
