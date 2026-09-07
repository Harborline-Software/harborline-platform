import type * as React from 'react'

export type NumericTextBoxValue = number | null
export type NumericTextBoxSize = 'sm' | 'md' | 'lg'
export type NumericTextBoxSizeAlias = NumericTextBoxSize | 'small' | 'medium' | 'large'
export type NumericTextBoxFillMode = 'solid' | 'outline' | 'flat'
export type NumericTextBoxRounded = 'small' | 'medium' | 'large' | 'full'

export interface NumericTextBoxProps
  extends Omit<React.HTMLAttributes<HTMLDivElement>,
    'children' | 'defaultValue' | 'dir' | 'id' | 'onBlur' | 'onChange' | 'onFocus'> {
  readonly value?: NumericTextBoxValue
  readonly defaultValue?: number
  readonly onChange?: (value: NumericTextBoxValue) => void
  readonly min?: number
  readonly max?: number
  readonly step?: number
  readonly decimals?: number
  readonly format?: string
  readonly locale?: string
  readonly currency?: string
  readonly spinners?: boolean
  readonly placeholder?: string
  readonly disabled?: boolean
  readonly readOnly?: boolean
  readonly size?: NumericTextBoxSizeAlias
  readonly fillMode?: NumericTextBoxFillMode
  readonly rounded?: NumericTextBoxRounded
  readonly id?: string
  readonly name?: string
  readonly required?: boolean
  readonly error?: boolean
  readonly onFocus?: React.FocusEventHandler<HTMLInputElement>
  readonly onBlur?: React.FocusEventHandler<HTMLInputElement>
  readonly incrementLabel?: string
  readonly decrementLabel?: string
  readonly 'aria-label'?: string
  readonly 'aria-labelledby'?: string
  readonly 'aria-describedby'?: string
}
