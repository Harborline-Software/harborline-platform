/**
 * Exact fixed-point decimal arithmetic for the `money.*` operators (SPINE-1 §1.4).
 * BigInt mantissa + base-10 scale — NOT IEEE-754 — so the canonical string form is
 * byte-identical to the .NET `MoneyDecimal` (BigInteger) port. Operands are decimal
 * strings or integers; a non-integer JS number is rejected (cannot recover exact
 * decimal cross-tier).
 */
const MAX_SIGNIFICANT_DIGITS = 4096 // runtime mantissa/scale bound (finding F4); identical to .NET

export class MoneyDecimal {
  private constructor(
    private readonly mantissa: bigint,
    private readonly scale: number,
  ) {}

  static parse(text: string): MoneyDecimal {
    const s = text.trim()
    if (s.length === 0) throw new Error('empty money literal')
    let neg = false
    let i = 0
    if (s[0] === '+' || s[0] === '-') {
      neg = s[0] === '-'
      i = 1
    }
    let digits = ''
    let scale = 0
    let seenDot = false
    let any = false
    for (; i < s.length; i++) {
      const c = s[i]
      if (c === '.') {
        if (seenDot) throw new Error('multiple decimal points')
        seenDot = true
        continue
      }
      if (c < '0' || c > '9') throw new Error(`invalid money literal '${text}'`)
      digits += c
      any = true
      if (seenDot) scale++
    }
    if (!any) throw new Error(`invalid money literal '${text}'`)
    if (digits.length > MAX_SIGNIFICANT_DIGITS) throw new Error(`money literal exceeds ${MAX_SIGNIFICANT_DIGITS} significant digits`)
    let mantissa = BigInt(digits.length === 0 ? '0' : digits)
    if (neg) mantissa = -mantissa
    return new MoneyDecimal(mantissa, scale)
  }

  static fromInteger(value: number): MoneyDecimal {
    return new MoneyDecimal(BigInt(value), 0)
  }

  private static align(a: MoneyDecimal, b: MoneyDecimal): [bigint, bigint, number] {
    const scale = Math.max(a.scale, b.scale)
    if (scale - a.scale > MAX_SIGNIFICANT_DIGITS || scale - b.scale > MAX_SIGNIFICANT_DIGITS) throw new Error('money alignment exceeds the size bound')
    const am = a.mantissa * 10n ** BigInt(scale - a.scale)
    const bm = b.mantissa * 10n ** BigInt(scale - b.scale)
    return [am, bm, scale]
  }

  add(o: MoneyDecimal): MoneyDecimal {
    const [am, bm, scale] = MoneyDecimal.align(this, o)
    return new MoneyDecimal(am + bm, scale)
  }

  sub(o: MoneyDecimal): MoneyDecimal {
    const [am, bm, scale] = MoneyDecimal.align(this, o)
    return new MoneyDecimal(am - bm, scale)
  }

  mul(o: MoneyDecimal): MoneyDecimal {
    if (this.size + o.size > MAX_SIGNIFICANT_DIGITS || this.scale + o.scale > MAX_SIGNIFICANT_DIGITS) throw new Error('money multiplication exceeds the size bound')
    return new MoneyDecimal(this.mantissa * o.mantissa, this.scale + o.scale)
  }

  /** Significant-digit count of the mantissa (the BigInt work-size proxy; finding F4). */
  get size(): number {
    const m = this.mantissa < 0n ? -this.mantissa : this.mantissa
    return m === 0n ? 1 : m.toString().length
  }

  /** Exact decimal ordering (aligned-mantissa compare) — decimal min/max aggregates (finding F7). */
  compare(o: MoneyDecimal): number {
    const [am, bm] = MoneyDecimal.align(this, o)
    return am < bm ? -1 : am > bm ? 1 : 0
  }

  toCanonicalString(): string {
    if (this.mantissa === 0n) return '0'
    const neg = this.mantissa < 0n
    let digits = (neg ? -this.mantissa : this.mantissa).toString()
    if (this.scale === 0) return neg ? '-' + digits : digits
    if (digits.length <= this.scale) {
      digits = '0'.repeat(this.scale - digits.length + 1) + digits
    }
    const dot = digits.length - this.scale
    const intPart = digits.slice(0, dot)
    const fracPart = digits.slice(dot).replace(/0+$/, '')
    const body = fracPart.length === 0 ? intPart : intPart + '.' + fracPart
    return neg && body !== '0' ? '-' + body : body
  }
}
