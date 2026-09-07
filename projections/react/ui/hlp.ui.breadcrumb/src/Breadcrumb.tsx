import * as React from 'react'

export interface BreadcrumbItem {
  id?: string
  label: string
  href?: string
  current?: boolean
}

export interface BreadcrumbProps
  extends Omit<React.HTMLAttributes<HTMLElement>, 'aria-label' | 'children'> {
  items: readonly BreadcrumbItem[]
  separator?: React.ReactNode
  accessibleLabel?: string
}

const classes = (...values: Array<string | undefined>) => values.filter(Boolean).join(' ')

function DefaultSeparator() {
  return (
    <svg
      aria-hidden="true"
      className="hl-breadcrumb__separator-icon"
      fill="none"
      focusable="false"
      viewBox="0 0 16 16"
    >
      <path d="m6 3.5 4 4.5-4 4.5" />
    </svg>
  )
}

export function Breadcrumb({
  items,
  separator,
  accessibleLabel = 'Breadcrumb',
  className,
  ...hostAttributes
}: BreadcrumbProps) {
  // Ticket 124: an empty trail renders nothing at all, not an empty landmark. A breadcrumb states
  // where the current page sits in a hierarchy; with no crumbs there is no location to state, so
  // there is no honest sentence to write (contrast hlp.ui.chart and hlp.ui.accordion, which keep a
  // frame the reader asked to be filled and therefore carry an `empty` prop). What was reachable
  // and undesigned here was the <nav aria-label="Breadcrumb"><ol/></nav> this used to emit: a
  // navigation landmark announced to assistive technology with nothing inside it.
  if (items.length === 0) return null

  const landmarkLabel = accessibleLabel.trim() || 'Breadcrumb'
  const renderedSeparator = separator ?? <DefaultSeparator />

  return (
    <nav
      {...hostAttributes}
      aria-label={landmarkLabel}
      className={classes('hl-breadcrumb', className)}
    >
      <ol className="hl-breadcrumb__list">
        {items.map((item, index) => {
          const current = item.current ?? index === items.length - 1
          const key = item.id ?? `${index}:${item.href ?? ''}:${item.label}`

          return (
            <li className="hl-breadcrumb__item" data-hl-current={current || undefined} key={key}>
              {index > 0 ? (
                <span aria-hidden="true" className="hl-breadcrumb__separator">
                  {renderedSeparator}
                </span>
              ) : null}
              {item.href && !current ? (
                <a className="hl-breadcrumb__link" href={item.href}>{item.label}</a>
              ) : (
                <span
                  aria-current={current ? 'page' : undefined}
                  className={classes('hl-breadcrumb__text', current ? 'hl-breadcrumb__text--current' : undefined)}
                >
                  {item.label}
                </span>
              )}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}
