import * as React from 'react'

import { Switch, type SwitchProps } from '@harborline-platform/hlp.ui.switch'

export interface SwitchFieldProps
  extends Omit<SwitchProps, 'checked' | 'defaultChecked' | 'name' | 'onChange' | 'onCheckedChange'> {
  readonly name: string
  readonly checked: boolean
  readonly onCheckedChange: (checked: boolean) => void
}

export const SwitchField = React.forwardRef<HTMLButtonElement, SwitchFieldProps>(function SwitchField(
  { name, checked, onCheckedChange, id, ...switchProps },
  forwardedRef,
) {
  if (!name.trim()) throw new Error('missing-name')
  return (
    <Switch
      {...switchProps}
      checked={checked}
      id={id ?? name}
      name={name}
      onCheckedChange={onCheckedChange}
      ref={forwardedRef}
    />
  )
})
