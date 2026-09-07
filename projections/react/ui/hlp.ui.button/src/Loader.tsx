import * as React from 'react'
import { cn } from './cn'

export type LoaderSize = 'xs' | 'sm'

export interface LoaderProps extends React.HTMLAttributes<HTMLSpanElement> {
  variant?: 'spinner'
  size?: LoaderSize
}

export function Loader({ variant = 'spinner', size = 'xs', className, ...props }: LoaderProps) {
  return (
    <span
      aria-hidden="true"
      data-loader-variant={variant}
      className={cn('inline-flex items-center justify-center', className)}
      {...props}
    >
      <span
        className={cn(
          'rounded-full border-muted border-t-primary animate-spin motion-reduce:animate-none',
          size === 'sm' ? 'h-4 w-4 border-2' : 'h-3 w-3 border-[1.5px]',
        )}
      />
    </span>
  )
}
