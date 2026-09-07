import * as React from 'react'
import { cn } from './cn'
import type { SizeAlias } from './size'
import { useHarborlineStrings } from './locale'

export type ButtonGroupSelection = 'none' | 'single' | 'multiple'

export interface ButtonGroupProps {
  /** FR-3: canonical 'sm'|'md'|'lg'; deprecated 'small'|'medium'|'large' still accepted */
  size?: SizeAlias
  disabled?: boolean
  selection?: ButtonGroupSelection
  value?: string | string[]
  defaultValue?: string | string[]
  onValueChange?: (value: string | string[]) => void
  children: React.ReactNode
  'aria-label'?: string
  'aria-labelledby'?: string
  className?: string
}

const ButtonGroupContext = React.createContext<{
  size?: SizeAlias
  disabled: boolean
  selection: ButtonGroupSelection
  selected: string[]
  onSelect: (value: string) => void
}>({
  disabled: false,
  selection: 'none',
  selected: [],
  onSelect: () => {},
})

export function useButtonGroupContext() {
  return React.useContext(ButtonGroupContext)
}

export function ButtonGroup({
  size,
  disabled = false,
  selection = 'none',
  value: controlledValue,
  defaultValue,
  onValueChange,
  children,
  'aria-label': ariaLabel,
  'aria-labelledby': ariaLabelledBy,
  className,
}: ButtonGroupProps) {
  const { direction } = useHarborlineStrings()
  const toArray = (v: string | string[] | undefined): string[] => {
    if (!v) return []
    return Array.isArray(v) ? v : [v]
  }

  const [internalSelected, setInternalSelected] = React.useState<string[]>(() =>
    toArray(defaultValue),
  )

  const selected = controlledValue !== undefined ? toArray(controlledValue) : internalSelected

  const onSelect = (val: string) => {
    if (selection === 'none') return
    let next: string[]
    if (selection === 'single') {
      next = selected.includes(val) ? [] : [val]
    } else {
      next = selected.includes(val) ? selected.filter(v => v !== val) : [...selected, val]
    }
    if (controlledValue === undefined) setInternalSelected(next)
    onValueChange?.(selection === 'single' ? (next[0] ?? '') : next)
  }

  return (
    <ButtonGroupContext.Provider value={{ size, disabled, selection, selected, onSelect }}>
      <div
        dir={direction}
        role={selection !== 'none' ? 'group' : undefined}
        aria-label={selection !== 'none' ? ariaLabel : undefined}
        aria-labelledby={selection !== 'none' ? ariaLabelledBy : undefined}
        className={cn(
          'inline-flex [&>*:not(:first-child)]:rounded-s-none [&>*:not(:last-child)]:rounded-e-none [&>*:not(:first-child)]:border-s-0',
          className,
        )}
      >
        {children}
      </div>
    </ButtonGroupContext.Provider>
  )
}
