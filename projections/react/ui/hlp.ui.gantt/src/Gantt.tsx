import * as React from 'react'

import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

import type {
  GanttColumn,
  GanttColumnField,
  GanttProps,
  GanttTask,
  GanttZoom,
} from './Gantt.types'
import {
  createGanttScale,
  dayToDate,
  prepareGantt,
  taskGeometry,
  type PreparedGantt,
} from './gantt-model'

const ZOOMS: readonly GanttZoom[] = ['day', 'week', 'month']

function classNames(base: string, extra?: string): string {
  return extra ? `${base} ${extra}` : base
}

function formatDate(day: number, locale: string, options: Intl.DateTimeFormatOptions): string {
  try {
    return new Intl.DateTimeFormat(locale, { ...options, timeZone: 'UTC' }).format(dayToDate(day))
  } catch {
    return new Intl.DateTimeFormat('en', { ...options, timeZone: 'UTC' }).format(dayToDate(day))
  }
}

function isoWeek(day: number): number {
  const date = dayToDate(day)
  const target = new Date(date.getTime())
  const weekday = target.getUTCDay() || 7
  target.setUTCDate(target.getUTCDate() + 4 - weekday)
  const yearStart = new Date(0)
  yearStart.setUTCHours(0, 0, 0, 0)
  yearStart.setUTCFullYear(target.getUTCFullYear(), 0, 1)
  return Math.ceil(((target.getTime() - yearStart.getTime()) / 86_400_000 + 1) / 7)
}

function defaultColumns(t: (key: string) => string): readonly GanttColumn[] {
  return [
    { field: 'title', title: t('gantt.title') },
    { field: 'start', title: t('gantt.start') },
    { field: 'end', title: t('gantt.end') },
  ]
}

function resolvedColumns(columns: readonly GanttColumn[] | null | undefined, t: (key: string) => string): readonly GanttColumn[] {
  const source = columns === null || columns === undefined ? defaultColumns(t) : columns
  return source.map(column => ({ ...column, title: column.title ?? t(`gantt.${column.field}`) }))
}

function columnValue(task: GanttTask, field: GanttColumnField, locale: string): React.ReactNode {
  if (field === 'title') return task.title
  if (field === 'progress') return `${task.progress ?? 0}%`
  const value = field === 'start' ? task.start : task.end
  const [year, month, day] = value.split('-').map(Number)
  const date = new Date(0)
  date.setUTCHours(0, 0, 0, 0)
  date.setUTCFullYear(year, month - 1, day)
  try {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeZone: 'UTC' }).format(date)
  } catch {
    return new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeZone: 'UTC' }).format(date)
  }
}

function scaleLabel(day: number, zoom: GanttZoom, locale: string, t: (key: string, vars?: Record<string, string | number>) => string): string {
  if (zoom === 'week') return t('gantt.weekNumber', { week: isoWeek(day) })
  return formatDate(day, locale, zoom === 'month'
    ? { month: 'short', year: 'numeric' }
    : { month: 'short', day: 'numeric' })
}

function DependencyLayer({ prepared, rowHeight }: { prepared: PreparedGantt; rowHeight: number }) {
  if (prepared.dependencies.length === 0 || prepared.tasks.length === 0) return null
  const count = prepared.tasks.length
  return (
    <svg
      aria-hidden="true"
      className="hl-gantt__dependencies"
      focusable="false"
      preserveAspectRatio="none"
      style={{ '--hl-gantt-dependencies-height': `${count * rowHeight}px` } as React.CSSProperties}
      viewBox="0 0 100 100"
    >
      {prepared.dependencies.map(({ dependency, fromIndex, toIndex }, index) => {
        const from = prepared.tasks[fromIndex]
        const to = prepared.tasks[toIndex]
        if (!from || !to) return null
        const fromGeometry = taskGeometry(from, prepared.minimumDay, prepared.maximumDay)
        const toGeometry = taskGeometry(to, prepared.minimumDay, prepared.maximumDay)
        return (
          <path
            className="hl-gantt__dependency"
            d={`M ${fromGeometry.offsetPercent + fromGeometry.widthPercent} ${(fromIndex + 0.5) / count * 100} L ${toGeometry.offsetPercent} ${(toIndex + 0.5) / count * 100}`}
            data-dependency-from={dependency.fromId}
            data-dependency-to={dependency.toId}
            key={`${dependency.fromId}-${dependency.toId}-${index}`}
            vectorEffect="non-scaling-stroke"
          />
        )
      })}
    </svg>
  )
}

export function Gantt({
  tasks = [],
  dependencies = [],
  columns,
  zoom = 'day',
  onZoomChange,
  showZoomPicker = false,
  rowHeight = 36,
  accessibleName,
  empty,
  className,
  dir,
  'aria-label': hostLabel,
  ...attributes
}: GanttProps) {
  const { direction, locale, t } = useHarborlineStrings()
  const paneColumns = React.useMemo(() => resolvedColumns(columns, t), [columns, t])
  const prepared = React.useMemo(() => prepareGantt(tasks, dependencies, paneColumns), [dependencies, paneColumns, tasks])
  const scale = React.useMemo(
    () => createGanttScale(prepared.minimumDay, prepared.maximumDay, zoom),
    [prepared.maximumDay, prepared.minimumDay, zoom],
  )
  const label = accessibleName?.trim() || (typeof hostLabel === 'string' ? hostLabel.trim() : '') || t('gantt.title')
  const noResults = empty ?? t('dataGrid.noResults')
  const effectiveRowHeight = Number.isFinite(rowHeight) && rowHeight > 0 ? rowHeight : 36
  const columnCount = paneColumns.length + 1
  const [activeCell, setActiveCell] = React.useState({ row: 0, column: 0 })
  const cellRefs = React.useRef(new Map<string, HTMLTableCellElement>())

  React.useEffect(() => {
    setActiveCell(current => ({
      row: Math.min(current.row, Math.max(0, prepared.tasks.length - 1)),
      column: Math.min(current.column, Math.max(0, columnCount - 1)),
    }))
  }, [columnCount, prepared.tasks.length])

  const moveCell = (event: React.KeyboardEvent<HTMLTableCellElement>, row: number, column: number) => {
    let nextRow = row
    let nextColumn = column
    if (event.key === 'ArrowDown') nextRow = Math.min(prepared.tasks.length - 1, row + 1)
    else if (event.key === 'ArrowUp') nextRow = Math.max(0, row - 1)
    else if (event.key === 'ArrowRight') nextColumn = Math.min(columnCount - 1, column + 1)
    else if (event.key === 'ArrowLeft') nextColumn = Math.max(0, column - 1)
    else if (event.key === 'Home') nextColumn = 0
    else if (event.key === 'End') nextColumn = columnCount - 1
    else return
    event.preventDefault()
    setActiveCell({ row: nextRow, column: nextColumn })
    cellRefs.current.get(`${nextRow}:${nextColumn}`)?.focus()
  }

  const cellProps = (row: number, column: number) => ({
    'data-gantt-cell': `${row}:${column}`,
    onFocus: () => setActiveCell({ row, column }),
    onKeyDown: (event: React.KeyboardEvent<HTMLTableCellElement>) => moveCell(event, row, column),
    ref: (element: HTMLTableCellElement | null) => {
      const key = `${row}:${column}`
      if (element) cellRefs.current.set(key, element)
      else cellRefs.current.delete(key)
    },
    tabIndex: activeCell.row === row && activeCell.column === column ? 0 : -1,
  })

  return (
    <div
      {...attributes}
      aria-label={label}
      className={classNames('hl-gantt', className)}
      data-read-only="true"
      data-zoom={zoom}
      dir={dir ?? direction}
      role="region"
      style={{ '--hl-gantt-row-height': `${effectiveRowHeight}px`, ...attributes.style } as React.CSSProperties}
    >
      {showZoomPicker ? (
        <label className="hl-gantt__zoom">
          <span>{t('gantt.zoom')}</span>
          <select aria-label={t('gantt.zoom')} onChange={event => onZoomChange?.(event.currentTarget.value as GanttZoom)} value={zoom}>
            {ZOOMS.map(value => <option key={value} value={value}>{t(`gantt.${value}`)}</option>)}
          </select>
        </label>
      ) : null}
      <div aria-label={label} className="hl-gantt__scroll" role="group" tabIndex={0}>
        <div className="hl-gantt__table-wrap">
          <table aria-colcount={columnCount} aria-label={label} aria-rowcount={prepared.tasks.length} className="hl-gantt__table">
            <caption className="hl-gantt__sr-only">{label}</caption>
            <thead>
              <tr>
                {paneColumns.map((column, index) => (
                  <th data-gantt-column={column.field} key={`${column.field}-${index}`} scope="col" style={column.width === undefined ? undefined : { inlineSize: `${column.width}px` }}>
                    {column.title}
                  </th>
                ))}
                <th className="hl-gantt__timeline-heading" scope="col">
                  <span className="hl-gantt__timeline-title">{t('gantt.name')}</span>
                  <span aria-hidden="true" className="hl-gantt__scale" data-scale-count={scale.length}>
                    {scale.map(mark => (
                      <span className="hl-gantt__scale-mark" key={mark.key} style={{ insetInlineStart: `${mark.offsetPercent}%` }}>
                        {scaleLabel(mark.day, zoom, locale, t)}
                      </span>
                    ))}
                  </span>
                </th>
              </tr>
            </thead>
            <tbody>
              {prepared.tasks.length === 0 ? (
                <tr><td className="hl-gantt__empty" colSpan={columnCount}>{noResults}</td></tr>
              ) : prepared.tasks.map((preparedTask, rowIndex) => {
                const { task } = preparedTask
                const geometry = taskGeometry(preparedTask, prepared.minimumDay, prepared.maximumDay)
                const progress = task.progress ?? 0
                const startText = columnValue(task, 'start', locale)
                const endText = columnValue(task, 'end', locale)
                return (
                  <tr data-task-id={task.id} data-task-row={rowIndex} key={task.id}>
                    {paneColumns.map((column, columnIndex) => (
                      <td {...cellProps(rowIndex, columnIndex)} data-gantt-column={column.field} key={`${task.id}-${column.field}-${columnIndex}`}>
                        {columnValue(task, column.field, locale)}
                      </td>
                    ))}
                    <td {...cellProps(rowIndex, paneColumns.length)} className="hl-gantt__timeline-cell">
                      <div
                        aria-label={`${task.title}: ${startText} – ${endText}, ${progress}%`}
                        className="hl-gantt__bar"
                        data-task-bar={task.id}
                        role="img"
                        style={{
                          '--hl-gantt-bar-color': task.color ?? 'var(--hl-gantt-accent)',
                          '--hl-gantt-bar-offset': `${geometry.offsetPercent}%`,
                          '--hl-gantt-bar-width': `${geometry.widthPercent}%`,
                        } as React.CSSProperties}
                      >
                        <span className="hl-gantt__bar-title">{task.title}</span>
                        <span className="hl-gantt__progress-text">{progress}%</span>
                        <span aria-hidden="true" className="hl-gantt__progress" style={{ inlineSize: `${progress}%` }} />
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          <DependencyLayer prepared={prepared} rowHeight={effectiveRowHeight} />
        </div>
      </div>
    </div>
  )
}
