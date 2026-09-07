import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'

export type TableDensity = 'sm' | 'md'

export interface TableProps extends React.TableHTMLAttributes<HTMLTableElement> {
  density?: TableDensity
}

export type TableHeadProps = React.HTMLAttributes<HTMLTableSectionElement>
export type TableBodyProps = React.HTMLAttributes<HTMLTableSectionElement>
export type TableRowProps = React.HTMLAttributes<HTMLTableRowElement>
export type TableHeaderCellProps = React.ThHTMLAttributes<HTMLTableCellElement>
export type TableCellProps = React.TdHTMLAttributes<HTMLTableCellElement>
export type TableCaptionProps = React.HTMLAttributes<HTMLTableCaptionElement>

const TableDensityContext = React.createContext<TableDensity>('md')

export const Table = React.forwardRef<HTMLTableElement, TableProps>(function Table(
  { density = 'md', className, children, ...tableAttributes },
  forwardedRef,
) {
  return (
    <TableDensityContext.Provider value={density}>
      <div className="hl-table__overflow" data-hl-presentational="true">
        <table
          {...tableAttributes}
          className={cn('hl-table', className)}
          data-hl-density={density}
          ref={forwardedRef}
        >
          {children}
        </table>
      </div>
    </TableDensityContext.Provider>
  )
})

export const TableHead = React.forwardRef<HTMLTableSectionElement, TableHeadProps>(function TableHead(
  { className, children, ...headAttributes },
  forwardedRef,
) {
  return (
    <thead {...headAttributes} className={cn('hl-table__head', className)} ref={forwardedRef}>
      {children}
    </thead>
  )
})

export const TableBody = React.forwardRef<HTMLTableSectionElement, TableBodyProps>(function TableBody(
  { className, children, ...bodyAttributes },
  forwardedRef,
) {
  return (
    <tbody {...bodyAttributes} className={cn('hl-table__body', className)} ref={forwardedRef}>
      {children}
    </tbody>
  )
})

export const TableRow = React.forwardRef<HTMLTableRowElement, TableRowProps>(function TableRow(
  { className, children, ...rowAttributes },
  forwardedRef,
) {
  return (
    <tr {...rowAttributes} className={cn('hl-table__row', className)} ref={forwardedRef}>
      {children}
    </tr>
  )
})

export const TableHeaderCell = React.forwardRef<HTMLTableCellElement, TableHeaderCellProps>(
  function TableHeaderCell({ className, children, scope = 'col', ...cellAttributes }, forwardedRef) {
    const density = React.useContext(TableDensityContext)
    return (
      <th
        {...cellAttributes}
        className={cn('hl-table__header-cell', className)}
        data-hl-density={density}
        ref={forwardedRef}
        scope={scope}
      >
        {children}
      </th>
    )
  },
)

export const TableCell = React.forwardRef<HTMLTableCellElement, TableCellProps>(function TableCell(
  { className, children, ...cellAttributes },
  forwardedRef,
) {
  const density = React.useContext(TableDensityContext)
  return (
    <td
      {...cellAttributes}
      className={cn('hl-table__cell', className)}
      data-hl-density={density}
      ref={forwardedRef}
    >
      {children}
    </td>
  )
})

export const TableCaption = React.forwardRef<HTMLTableCaptionElement, TableCaptionProps>(
  function TableCaption({ className, children, ...captionAttributes }, forwardedRef) {
    return (
      <caption
        {...captionAttributes}
        className={cn('hl-table__caption', className)}
        ref={forwardedRef}
      >
        {children}
      </caption>
    )
  },
)
