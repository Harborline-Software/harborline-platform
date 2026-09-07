import { useId, useRef, useState } from 'react'
import { useOutsideClick } from '@harborline-platform/hlp.ui.use-outside-click'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

export type ExportFormat = 'csv' | 'xlsx' | 'pdf' | 'json' | 'md'
export interface DataExportButtonProps { formats?: readonly ExportFormat[]; onExport: (format: ExportFormat) => void | Promise<void>; label?: string; disabled?: boolean; loading?: boolean; className?: string }
const formatKeys: Record<ExportFormat, string> = { csv: 'buttons.format.csv', xlsx: 'buttons.format.xlsx', pdf: 'buttons.format.pdf', json: 'buttons.format.json', md: 'buttons.format.md' }

export function DataExportButton({ formats = ['csv', 'xlsx'], onExport, label, disabled = false, loading = false, className }: DataExportButtonProps) {
  const { resolveString } = useHarborlineStrings()
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState<ExportFormat | null>(null)
  const root = useRef<HTMLDivElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)
  const items = useRef<Array<HTMLButtonElement | null>>([])
  const menuId = useId()
  const inert = disabled || loading || busy !== null
  const close = (restore = false) => { setOpen(false); if (restore) queueMicrotask(() => trigger.current?.focus()) }
  useOutsideClick(root, () => close(false), { enabled: open })
  const focusIndex = (index: number) => items.current[(index + formats.length) % formats.length]?.focus()
  const invoke = async (format: ExportFormat) => { if (inert) return; setOpen(false); setBusy(format); try { await onExport(format) } finally { setBusy(null); queueMicrotask(() => trigger.current?.focus()) } }
  if (formats.length === 1) {
    const format = formats[0]
    return <button ref={trigger} type="button" className={`hl-data-export hl-data-export--direct${className ? ` ${className}` : ''}`} aria-busy={loading || busy !== null || undefined} disabled={inert} onClick={() => void invoke(format)}>{loading || busy ? resolveString(undefined, 'common.loading') : resolveString(label, 'buttons.export')}</button>
  }
  return (
    <div ref={root} className={`hl-data-export${className ? ` ${className}` : ''}`}>
      <button ref={trigger} type="button" aria-haspopup="menu" aria-expanded={open} aria-controls={menuId} aria-busy={loading || busy !== null || undefined} disabled={inert} onClick={() => setOpen(value => !value)} onKeyDown={event => { if (event.key === 'ArrowDown' || event.key === 'ArrowUp') { event.preventDefault(); setOpen(true); queueMicrotask(() => focusIndex(event.key === 'ArrowDown' ? 0 : formats.length - 1)) } }}>
        {loading || busy ? resolveString(undefined, 'common.loading') : resolveString(label, 'buttons.export')}
      </button>
      {open && <div id={menuId} role="menu" className="hl-data-export__menu" onKeyDown={event => { const index = items.current.indexOf(document.activeElement as HTMLButtonElement); if (event.key === 'Escape') { event.preventDefault(); close(true) } else if (event.key === 'ArrowDown') { event.preventDefault(); focusIndex(index + 1) } else if (event.key === 'ArrowUp') { event.preventDefault(); focusIndex(index - 1) } }}>
        {formats.map((format, index) => <button key={format} ref={element => { items.current[index] = element }} type="button" role="menuitem" data-format={format} onClick={() => void invoke(format)}>{resolveString(undefined, formatKeys[format])}</button>)}
      </div>}
    </div>
  )
}
