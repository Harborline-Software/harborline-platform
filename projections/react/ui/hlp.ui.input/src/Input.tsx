import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'
import { useTouchSizing } from '@harborline-platform/hlp.ui.use-can-show-master-detail'

export type InputSize = 'sm' | 'md' | 'lg' | 'small' | 'medium' | 'large'
export type InputFillMode = 'solid' | 'outline' | 'flat'
export type InputRounding = 'small' | 'medium' | 'large' | 'full'

export type InputProps = Omit<React.InputHTMLAttributes<HTMLInputElement>, 'prefix' | 'size'> & {
  size?: InputSize
  fillMode?: InputFillMode
  rounded?: InputRounding
  prefix?: React.ReactNode
  suffix?: React.ReactNode
  invalid?: boolean
}

const sizes: Record<InputSize, 'sm' | 'md' | 'lg'> = {
  sm: 'sm', small: 'sm', md: 'md', medium: 'md', lg: 'lg', large: 'lg',
}
const fillModes = new Set<InputFillMode>(['solid', 'outline', 'flat'])
const roundings = new Set<InputRounding>(['small', 'medium', 'large', 'full'])

export const Input = React.forwardRef<HTMLInputElement, InputProps>(function Input(
  {
    size = 'md', fillMode = 'solid', rounded = 'medium', prefix, suffix,
    invalid = false, className, disabled, onChange, ...attributes
  },
  ref,
) {
  if (!(size in sizes) || !fillModes.has(fillMode) || !roundings.has(rounded)) {
    throw new Error('unsupported-input-appearance')
  }
  const touchSized = useTouchSizing()
  const input = (
    <input
      {...attributes}
      ref={ref}
      disabled={disabled}
      onChange={disabled ? undefined : onChange}
      className={cn('hl-input__control', className)}
      data-hl-size={sizes[size]}
      data-hl-fill={fillMode}
      data-hl-rounded={rounded}
      data-hl-invalid={invalid || undefined}
      data-hl-touch={touchSized || undefined}
    />
  )

  if (prefix === undefined && suffix === undefined) return input
  return (
    <span
      className="hl-input"
      data-hl-size={sizes[size]}
      data-hl-fill={fillMode}
      data-hl-rounded={rounded}
      data-hl-invalid={invalid || undefined}
      data-hl-touch={touchSized || undefined}
    >
      {prefix === undefined ? null : <span className="hl-input__prefix">{prefix}</span>}
      {React.cloneElement(input, { className: cn('hl-input__control', 'hl-input__control--adorned', className) })}
      {suffix === undefined ? null : <span className="hl-input__suffix">{suffix}</span>}
    </span>
  )
})
