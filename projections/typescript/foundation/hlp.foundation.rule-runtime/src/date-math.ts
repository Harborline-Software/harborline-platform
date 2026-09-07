/**
 * Deterministic, timezone-free calendar arithmetic for the `date.*` operators
 * (SPINE-1 §1.4). Dates are `YYYY-MM-DD` (proleptic Gregorian, UTC, date-only —
 * DST structurally excluded). Howard Hinnant's days-from-civil algorithm, so leap
 * years are exact and the result is byte-identical to the .NET `DateMath` port.
 */

function parse(text: string): [number, number, number] {
  const parts = (text ?? '').split('-')
  if (parts.length >= 3) {
    let dayPart = parts[2]
    const tIdx = dayPart.indexOf('T')
    if (tIdx >= 0) dayPart = dayPart.slice(0, tIdx)
    const y = Number(parts[0])
    const m = Number(parts[1])
    const d = Number(dayPart)
    if (
      Number.isInteger(y) && Number.isInteger(m) && Number.isInteger(d) &&
      m >= 1 && m <= 12 && d >= 1 && d <= 31
    ) {
      return [y, m, d]
    }
  }
  throw new Error(`invalid date '${text}'`)
}

export function epochDay(y: number, m: number, d: number): number {
  const yy = m <= 2 ? y - 1 : y
  const era = Math.floor((yy >= 0 ? yy : yy - 399) / 400)
  const yoe = yy - era * 400
  const doy = Math.floor((153 * (m > 2 ? m - 3 : m + 9) + 2) / 5) + d - 1
  const doe = yoe * 365 + Math.floor(yoe / 4) - Math.floor(yoe / 100) + doy
  return era * 146097 + doe - 719468
}

function fromEpochDay(z0: number): [number, number, number] {
  const z = z0 + 719468
  const era = Math.floor((z >= 0 ? z : z - 146096) / 146097)
  const doe = z - era * 146097
  const yoe = Math.floor((doe - Math.floor(doe / 1460) + Math.floor(doe / 36524) - Math.floor(doe / 146096)) / 365)
  const y = yoe + era * 400
  const doy = doe - (365 * yoe + Math.floor(yoe / 4) - Math.floor(yoe / 100))
  const mp = Math.floor((5 * doy + 2) / 153)
  const d = doy - Math.floor((153 * mp + 2) / 5) + 1
  const m = mp < 10 ? mp + 3 : mp - 9
  return [m <= 2 ? y + 1 : y, m, d]
}

function isLeap(y: number): boolean {
  return (y % 4 === 0 && y % 100 !== 0) || y % 400 === 0
}

function daysInMonth(y: number, m: number): number {
  const lengths = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
  if (m === 2 && isLeap(y)) return 29
  return lengths[m - 1]
}

function fmt(y: number, m: number, d: number): string {
  const p = (n: number, w: number) => String(n).padStart(w, '0')
  return `${p(y, 4)}-${p(m, 2)}-${p(d, 2)}`
}

export function today(nowUtc: Date): string {
  return fmt(nowUtc.getUTCFullYear(), nowUtc.getUTCMonth() + 1, nowUtc.getUTCDate())
}

export function dateAdd(date: string, n: number, unit: string): string {
  const [y, m, d] = parse(date)
  switch (unit) {
    case 'day': {
      const [ny, nm, nd] = fromEpochDay(epochDay(y, m, d) + n)
      return fmt(ny, nm, nd)
    }
    case 'month': {
      const total = y * 12 + (m - 1) + n
      let yr = Math.floor(total / 12)
      let mo = total % 12
      if (mo < 0) {
        mo += 12
        yr -= 1
      }
      const month = mo + 1
      const day = Math.min(d, daysInMonth(yr, month))
      return fmt(yr, month, day)
    }
    case 'year': {
      const yr = y + n
      const day = Math.min(d, daysInMonth(yr, m))
      return fmt(yr, m, day)
    }
    default:
      throw new Error(`unknown date unit '${unit}'`)
  }
}

export function dateDiffDays(a: string, b: string): number {
  const [ay, am, ad] = parse(a)
  const [by, bm, bd] = parse(b)
  return epochDay(ay, am, ad) - epochDay(by, bm, bd)
}
