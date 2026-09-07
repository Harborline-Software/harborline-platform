import type { SVGAttributes } from 'react'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

export type SpinnerSize = 'xs' | 'sm' | 'md' | 'lg'
export type SpinnerThemeColor = 'primary' | 'secondary' | 'tertiary' | 'info' | 'success' | 'warning' | 'error' | 'inverse'
export type SpinnerType = 'ring' | 'converging'
export interface SpinnerProps extends SVGAttributes<SVGSVGElement> { size?: SpinnerSize; label?: string; themeColor?: SpinnerThemeColor; type?: SpinnerType }
const classes = (...values: Array<string | undefined>) => values.filter(Boolean).join(' ')

export function Spinner({ size = 'md', label, themeColor, type = 'ring', className, ...host }: SpinnerProps) {
  const { resolveString } = useHarborlineStrings()
  const name = label === undefined ? `${resolveString(undefined, 'common.loading')}…` : label
  return (
    <svg {...host} role="status" aria-label={name} viewBox="0 0 24 24" fill="none" className={classes('hl-spinner', `hl-spinner--${size}`, `hl-spinner--${type}`, themeColor ? `hl-spinner--${themeColor}` : undefined, className)} data-hl-size={size} data-hl-type={type}>
      <circle className="hl-spinner__track" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="3" />
      <path className="hl-spinner__arc" d="M12 2a10 10 0 0 1 10 10" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
      {type === 'converging' && <path className="hl-spinner__arc hl-spinner__arc--reverse" d="M12 22A10 10 0 0 1 2 12" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />}
    </svg>
  )
}
