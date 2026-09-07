/**
 * Advisory linting for the decision-table editor (design §2.3 L1 guard + §2.4 gap/overlap linter).
 * These are AUTHORING-TIME hints surfaced inline; the load-bearing rejection is still the skin
 * compiler (`compileDecisionTable`, which raises `rule.skin.no_match_unresolved` and rejects a
 * non-terminal catch-all). The lint mirrors those checks so the author sees the problem BEFORE they
 * hit publish, plus the gap/overlap advisories the compiler does not raise (an overlap is legal under
 * a hit policy; a gap is only a problem if Otherwise is also unresolved).
 */

import type { DecisionTableDraft, TableCell } from './model.js'

/** A lint finding — a stable code + scalar params (localized by the surface, never English prose). */
export interface TableLintFinding {
  code: string
  severity: 'error' | 'warn' | 'info'
  /** The row id the finding anchors to, when row-specific. */
  rowId?: string
  params: Record<string, string>
}

/** Stable lint codes (localized off `rules.lint.*`). */
export const RuleLintCodes = {
  noMatchUnresolved: 'rules.lint.no_match_unresolved',
  nonTerminalCatchAll: 'rules.lint.non_terminal_catch_all',
  emptyOutput: 'rules.lint.empty_output',
  gap: 'rules.lint.gap',
  overlap: 'rules.lint.overlap',
} as const

function isCatchAllRow(cells: Record<string, TableCell>, columnIds: string[]): boolean {
  return columnIds.every((id) => {
    const c = cells[id]
    return !c || c.kind === 'any'
  })
}

/** True iff no-match is structurally resolved (a filled default OR a terminal catch-all row) — the
 * §2.3 publish gate, computed for the surface so it can block publish before the compiler does. */
export function noMatchResolved(draft: DecisionTableDraft): boolean {
  if (draft.noMatch.kind === 'default') return draft.noMatch.value.trim() !== ''
  // catch-all posture: the LAST row must be an unconditional catch-all.
  const columnIds = draft.columns.map((c) => c.id)
  const last = draft.rows[draft.rows.length - 1]
  return last !== undefined && isCatchAllRow(last.cells, columnIds)
}

/**
 * The full advisory finding set for a table draft. Order: structural (no-match, L1) then interval
 * gap/overlap on the first numeric column (the design's reified-interval example).
 */
export function lintTable(draft: DecisionTableDraft): TableLintFinding[] {
  const findings: TableLintFinding[] = []
  const columnIds = draft.columns.map((c) => c.id)

  // no-match unresolved (mirrors the compiler's publish gate)
  if (!noMatchResolved(draft)) {
    findings.push({ code: RuleLintCodes.noMatchUnresolved, severity: 'error', params: {} })
  }

  // L1: a non-terminal catch-all — an unconditional row that isn't the last row can never let rows
  // below it fire (design §2.3; the #1831 forward-guard, surfaced).
  draft.rows.forEach((row, idx) => {
    if (idx < draft.rows.length - 1 && isCatchAllRow(row.cells, columnIds)) {
      findings.push({ code: RuleLintCodes.nonTerminalCatchAll, severity: 'error', rowId: row.id, params: { row: String(idx + 1) } })
    }
    if (row.output.trim() === '') {
      findings.push({ code: RuleLintCodes.emptyOutput, severity: 'error', rowId: row.id, params: { row: String(idx + 1) } })
    }
  })

  // gap / overlap on the FIRST numeric column (design §2.4 — the interval sharp edge). Advisory only.
  const numericCol = draft.columns.find((c) => c.valueType === 'number')
  if (numericCol) {
    findings.push(...intervalFindings(draft, numericCol.id, columnIds))
  }
  return findings
}

interface Interval {
  rowId: string
  index: number
  lo: number
  hi: number
}

/** Collect the [lo, hi) interval of each row that constrains ONLY the numeric column (other cells
 * `any`), then flag adjacent gaps + overlaps. Rows that also constrain other columns are skipped
 * (their interval is conditional on another dimension — out of scope for the 1-D linter). */
function intervalFindings(draft: DecisionTableDraft, colId: string, columnIds: string[]): TableLintFinding[] {
  const otherCols = columnIds.filter((id) => id !== colId)
  const intervals: Interval[] = []
  draft.rows.forEach((row, index) => {
    const cell = row.cells[colId]
    if (!cell || cell.kind !== 'range') return
    const constrainsOthers = otherCols.some((id) => {
      const c = row.cells[id]
      return c && c.kind !== 'any'
    })
    if (constrainsOthers) return
    const lo = cell.lo.trim() === '' ? -Infinity : Number(cell.lo)
    const hi = cell.hi.trim() === '' ? Infinity : Number(cell.hi)
    if (!Number.isNaN(lo) && !Number.isNaN(hi)) intervals.push({ rowId: row.id, index, lo, hi })
  })

  intervals.sort((a, b) => a.lo - b.lo)
  const findings: TableLintFinding[] = []
  for (let i = 1; i < intervals.length; i++) {
    const prev = intervals[i - 1]
    const cur = intervals[i]
    if (cur.lo > prev.hi) {
      findings.push({ code: RuleLintCodes.gap, severity: 'warn', params: { from: fmt(prev.hi), to: fmt(cur.lo) } })
    } else if (cur.lo < prev.hi) {
      findings.push({ code: RuleLintCodes.overlap, severity: 'info', params: { at: fmt(cur.lo), to: fmt(prev.hi) } })
    }
  }
  return findings
}

function fmt(n: number): string {
  if (n === Infinity) return '∞'
  if (n === -Infinity) return '-∞'
  return String(n)
}
