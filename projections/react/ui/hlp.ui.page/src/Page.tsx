import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'

export interface PageProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'title'> {
  title: React.ReactNode
  subtitle?: React.ReactNode
  actions?: React.ReactNode
  titleAs?: 'h1' | 'h2' | 'h3'
  sticky?: boolean
  bodyPadding?: 'none' | 'sm' | 'md' | 'lg'
  bodyClassName?: string
}

const headings = new Set(['h1', 'h2', 'h3'])
const paddings = new Set(['none', 'sm', 'md', 'lg'])

function hasAccessibleText(value: React.ReactNode): boolean {
  return React.Children.toArray(value).some(child =>
    typeof child === 'string' || typeof child === 'number' ? String(child).trim().length > 0 : React.isValidElement(child),
  )
}

export function Page({
  title, subtitle, actions, titleAs = 'h1', sticky = true, bodyPadding = 'md',
  bodyClassName, className, children, ...attributes
}: PageProps) {
  if (!hasAccessibleText(title)) throw new Error('page-title-required')
  if (!headings.has(titleAs) || !paddings.has(bodyPadding)) throw new Error('unsupported-page-option')
  const Heading = titleAs
  return (
    <div {...attributes} className={cn('hl-page', className)}>
      <header className="hl-page__header" data-hl-sticky={sticky || undefined}>
        <div className="hl-page__heading-copy">
          <Heading className="hl-page__title">{title}</Heading>
          {subtitle === undefined ? null : <div className="hl-page__subtitle">{subtitle}</div>}
        </div>
        {actions === undefined ? null : <div className="hl-page__actions">{actions}</div>}
      </header>
      <section tabIndex={0} className={cn('hl-page__body', bodyClassName)} data-hl-padding={bodyPadding}>{children}</section>
    </div>
  )
}
