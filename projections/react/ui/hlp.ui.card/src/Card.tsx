import * as React from 'react'

export type CardVariant = 'flat' | 'raised' | 'outlined' | 'elevated'
export type CardPadding = 'none' | 'sm' | 'md' | 'lg'
export type CardOrientation = 'vertical' | 'horizontal'

export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  variant?: CardVariant
  padding?: CardPadding
  orientation?: CardOrientation
  separators?: boolean
  asChild?: boolean
  children: React.ReactNode
}

export interface CardHeaderProps extends React.HTMLAttributes<HTMLDivElement> {
  children: React.ReactNode
}

export interface CardTitleProps extends React.HTMLAttributes<HTMLHeadingElement> {
  as?: 'h1' | 'h2' | 'h3' | 'h4' | 'h5' | 'h6'
  children: React.ReactNode
}

export interface CardDescriptionProps extends React.HTMLAttributes<HTMLParagraphElement> {
  children: React.ReactNode
}

export interface CardContentProps extends React.HTMLAttributes<HTMLDivElement> {
  children: React.ReactNode
}

export interface CardFooterProps extends React.HTMLAttributes<HTMLDivElement> {
  children: React.ReactNode
}

interface CardContextValue {
  orientation: CardOrientation
  padding: CardPadding
}

const CardContext = React.createContext<CardContextValue>({ orientation: 'vertical', padding: 'md' })
const classes = (...values: Array<string | undefined>) => values.filter(Boolean).join(' ')

interface SlottableProps extends React.HTMLAttributes<HTMLElement> {
  [attribute: `data-${string}`]: string | undefined
}

function renderAsChild(
  children: React.ReactNode,
  rootProps: SlottableProps,
): React.ReactElement {
  const child = React.Children.only(children)
  if (!React.isValidElement<SlottableProps>(child)) throw new Error('card-as-child-requires-element')

  const mergedProps: Record<string, unknown> = {
    ...rootProps,
    ...child.props,
    className: classes(rootProps.className, child.props.className),
    style: { ...rootProps.style, ...child.props.style },
  }
  const rootRecord = rootProps as unknown as Record<string, unknown>
  const childRecord = child.props as unknown as Record<string, unknown>

  for (const name of Object.keys(childRecord)) {
    const rootHandler = rootRecord[name]
    const childHandler = childRecord[name]
    if (/^on[A-Z]/.test(name) && typeof rootHandler === 'function' && typeof childHandler === 'function') {
      mergedProps[name] = (...args: unknown[]) => {
        childHandler(...args)
        rootHandler(...args)
      }
    }
  }

  return React.cloneElement(child, mergedProps as unknown as Partial<SlottableProps>)
}

export function Card({
  variant = 'outlined',
  padding = 'md',
  orientation = 'vertical',
  separators = false,
  asChild = false,
  children,
  className,
  ...hostAttributes
}: CardProps) {
  const rootProps: SlottableProps = {
    ...hostAttributes,
    className: classes('hl-card', className),
    'data-hl-variant': variant,
    'data-hl-padding': padding,
    'data-hl-orientation': orientation,
    'data-hl-separators': separators ? 'true' : 'false',
  }

  return (
    <CardContext.Provider value={{ orientation, padding }}>
      {asChild ? renderAsChild(children, rootProps) : <div {...rootProps}>{children}</div>}
    </CardContext.Provider>
  )
}

export function CardHeader({ children, className, ...hostAttributes }: CardHeaderProps) {
  const { padding } = React.useContext(CardContext)
  return (
    <div
      {...hostAttributes}
      className={classes('hl-card__header', className)}
      data-hl-padding={padding}
    >
      {children}
    </div>
  )
}

export function CardTitle({ as: Heading = 'h3', children, className, ...hostAttributes }: CardTitleProps) {
  return (
    <Heading {...hostAttributes} className={classes('hl-card__title', className)}>
      {children}
    </Heading>
  )
}

/** React compatibility facade; not part of the four-symbol cross-projection revision. */
export function CardDescription({ children, className, ...hostAttributes }: CardDescriptionProps) {
  return (
    <p {...hostAttributes} className={classes('hl-card__description', className)}>
      {children}
    </p>
  )
}

export function CardContent({ children, className, ...hostAttributes }: CardContentProps) {
  const { orientation, padding } = React.useContext(CardContext)
  return (
    <div
      {...hostAttributes}
      className={classes('hl-card__content', className)}
      data-hl-flex={orientation === 'horizontal' ? 'true' : 'false'}
      data-hl-padding={padding}
    >
      {children}
    </div>
  )
}

/** React compatibility facade; not part of the four-symbol cross-projection revision. */
export function CardFooter({ children, className, ...hostAttributes }: CardFooterProps) {
  const { padding } = React.useContext(CardContext)
  return (
    <div
      {...hostAttributes}
      className={classes('hl-card__footer', className)}
      data-hl-padding={padding}
    >
      {children}
    </div>
  )
}
