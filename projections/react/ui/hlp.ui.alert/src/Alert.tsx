import type { HTMLAttributes, ReactNode } from 'react'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

export type AlertVariant = 'info' | 'success' | 'warning' | 'error'

export interface AlertProps extends Omit<HTMLAttributes<HTMLDivElement>, 'role' | 'title'> {
  variant?: AlertVariant
  title?: string
  closable?: boolean
  onClose?: () => void
  action?: ReactNode
  children: ReactNode
  dismissLabel?: string
}

const classes = (...values: Array<string | undefined | false>) => values.filter(Boolean).join(' ')
const urgent = new Set<AlertVariant>(['warning', 'error'])

function AlertIcon({ variant }: { variant: AlertVariant }) {
  return <svg className="hl-alert__icon" viewBox="0 0 20 20" fill="none" aria-hidden="true">
    {variant === 'success' && <><path d="M5.75 10.25 8.5 13l5.75-6" /><circle cx="10" cy="10" r="7.25" /></>}
    {variant === 'warning' && <><path d="M10 6.75v4.5M10 14.25h.01" /><path d="M8.73 3.8 2.5 14.6a1.5 1.5 0 0 0 1.3 2.25h12.4a1.5 1.5 0 0 0 1.3-2.25L11.27 3.8a1.47 1.47 0 0 0-2.54 0Z" /></>}
    {variant === 'error' && <><path d="m7.5 7.5 5 5m0-5-5 5" /><circle cx="10" cy="10" r="7.25" /></>}
    {variant === 'info' && <><path d="M10 9v4.5M10 6.5h.01" /><circle cx="10" cy="10" r="7.25" /></>}
  </svg>
}

export function Alert({ variant = 'info', title, closable = false, onClose, action, children, dismissLabel, className, ...host }: AlertProps) {
  const { resolveString } = useHarborlineStrings()
  return (
    <div {...host} role={urgent.has(variant) ? 'alert' : 'status'} className={classes('hl-alert', `hl-alert--${variant}`, className)} data-hl-variant={variant}>
      <span className="hl-alert__marker"><AlertIcon variant={variant} /></span>
      <div className="hl-alert__content">
        {title && <p className="hl-alert__title">{title}</p>}
        <div className="hl-alert__body">{children}</div>
        {action && <div className="hl-alert__action">{action}</div>}
      </div>
      {closable && <button type="button" className="hl-alert__dismiss" aria-label={resolveString(dismissLabel, 'common.dismiss')} onClick={onClose}><svg viewBox="0 0 20 20" aria-hidden="true"><path d="m6 6 8 8m0-8-8 8" /></svg></button>}
    </div>
  )
}
