import type * as React from 'react'

export type GanttZoom = 'day' | 'week' | 'month'
export type GanttCalendarDate = `${number}-${number}-${number}`
export type GanttColumnField = 'title' | 'start' | 'end' | 'progress'
export type GanttColor = `var(--${string})`

export interface GanttTask {
  readonly id: string
  readonly title: string
  readonly start: GanttCalendarDate
  readonly end: GanttCalendarDate
  readonly progress?: number
  readonly color?: GanttColor
}

export interface GanttDependency {
  readonly fromId: string
  readonly toId: string
}

export interface GanttColumn {
  readonly field: GanttColumnField
  readonly title?: string
  readonly width?: number
}

export interface GanttProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'children' | 'onChange'> {
  readonly tasks?: readonly GanttTask[]
  readonly dependencies?: readonly GanttDependency[]
  readonly columns?: readonly GanttColumn[] | null
  readonly zoom?: GanttZoom
  readonly onZoomChange?: (zoom: GanttZoom) => void
  readonly showZoomPicker?: boolean
  readonly rowHeight?: number
  readonly accessibleName?: string
  readonly empty?: string
}
