import * as React from 'react'

export type AccordionType = 'single' | 'multiple'

export interface AccordionItem {
  value: string
  title: React.ReactNode
  children: React.ReactNode
  disabled?: boolean
}

export interface AccordionProps extends React.HTMLAttributes<HTMLDivElement> {
  items: AccordionItem[]
  type?: AccordionType
  defaultValue?: string[]
  collapsible?: boolean
  value?: string[]
  onValueChange?: (value: string[]) => void
  /** Sentence drawn when `items` is empty, in place of a bordered box with nothing in it. */
  empty?: string
}

// No panels is a state a reader arrives at; the group says so rather than drawing an empty frame.
// The accordion declares no locale-provider dependency, so the default is the projection's own
// sentence rather than a catalog key -- unlike hlp.ui.chart (ticket 120), which already had one.
const EMPTY_DEFAULT = 'No sections to show.'

const classes = (...values: Array<string | undefined>) => values.filter(Boolean).join(' ')

function initialValues(
  items: readonly AccordionItem[],
  type: AccordionType,
  collapsible: boolean,
  defaults: readonly string[],
): string[] {
  const enabled = new Set(items.filter(item => !item.disabled).map(item => item.value))

  if (type === 'single') {
    const requested = defaults.find(value => enabled.has(value))
    if (requested !== undefined) return [requested]
    if (!collapsible) {
      const first = items.find(item => !item.disabled)
      return first ? [first.value] : []
    }
    return []
  }

  const requested = new Set(defaults.filter(value => enabled.has(value)))
  return items.filter(item => requested.has(item.value)).map(item => item.value)
}

function orderedValues(items: readonly AccordionItem[], values: ReadonlySet<string>): string[] {
  return items.filter(item => values.has(item.value)).map(item => item.value)
}

function idPart(value: string): string {
  const normalized = value.replace(/[^A-Za-z0-9_-]/g, '-')
  return normalized || 'item'
}

export function Accordion({
  items,
  type = 'single',
  defaultValue = [],
  collapsible = true,
  value,
  onValueChange,
  empty,
  className,
  ...hostAttributes
}: AccordionProps) {
  const isControlled = value !== undefined
  const [uncontrolledValues, setUncontrolledValues] = React.useState<string[]>(() =>
    initialValues(items, type, collapsible, defaultValue),
  )
  const openValues = isControlled ? value : uncontrolledValues
  const openSet = new Set(openValues)
  const headerRefs = React.useRef<Array<HTMLButtonElement | null>>([])
  const instanceId = React.useId().replace(/:/g, '')

  if (items.length === 0) {
    return (
      <div {...hostAttributes} className={classes('hl-accordion', className)} data-hl-type={type}>
        <p className="hl-accordion__empty" data-hl-empty="true">{empty?.trim() ? empty : EMPTY_DEFAULT}</p>
      </div>
    )
  }

  const toggle = (item: AccordionItem) => {
    if (item.disabled) return

    const next = new Set(openSet)
    if (next.has(item.value)) {
      if (type === 'single' && !collapsible) return
      next.delete(item.value)
    } else {
      if (type === 'single') next.clear()
      next.add(item.value)
    }

    const snapshot = orderedValues(items, next)
    if (isControlled) onValueChange?.(snapshot)
    else setUncontrolledValues(snapshot)
  }

  const moveFocus = (event: React.KeyboardEvent<HTMLButtonElement>, itemIndex: number) => {
    const enabledIndexes = items.flatMap((item, index) => item.disabled ? [] : [index])
    const position = enabledIndexes.indexOf(itemIndex)
    if (position < 0 || enabledIndexes.length === 0) return

    let target: number | undefined
    switch (event.key) {
      case 'ArrowDown':
        target = enabledIndexes[(position + 1) % enabledIndexes.length]
        break
      case 'ArrowUp':
        target = enabledIndexes[(position - 1 + enabledIndexes.length) % enabledIndexes.length]
        break
      case 'Home':
        target = enabledIndexes[0]
        break
      case 'End':
        target = enabledIndexes[enabledIndexes.length - 1]
        break
      default:
        return
    }

    event.preventDefault()
    if (target !== undefined) headerRefs.current[target]?.focus()
  }

  return (
    <div
      {...hostAttributes}
      className={classes('hl-accordion', className)}
      data-hl-type={type}
    >
      {items.map((item, index) => {
        const expanded = openSet.has(item.value)
        const key = `${instanceId}-${index}-${idPart(item.value)}`
        const headerId = `hl-accordion-${key}-header`
        const panelId = `hl-accordion-${key}-panel`

        return (
          <div className="hl-accordion__item" data-hl-expanded={expanded} key={item.value}>
            <h3 className="hl-accordion__heading">
              <button
                aria-controls={panelId}
                aria-expanded={expanded}
                className="hl-accordion__trigger"
                disabled={item.disabled}
                id={headerId}
                onClick={() => toggle(item)}
                onKeyDown={event => moveFocus(event, index)}
                ref={element => { headerRefs.current[index] = element }}
                type="button"
              >
                <span className="hl-accordion__title">{item.title}</span>
                <svg
                  aria-hidden="true"
                  className="hl-accordion__chevron"
                  fill="none"
                  viewBox="0 0 16 16"
                >
                  <path d="m4 6 4 4 4-4" />
                </svg>
              </button>
            </h3>
            <div
              aria-labelledby={headerId}
              className="hl-accordion__panel"
              hidden={!expanded}
              id={panelId}
              role="region"
            >
              <div className="hl-accordion__content">{item.children}</div>
            </div>
          </div>
        )
      })}
    </div>
  )
}
