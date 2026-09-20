/** Advisory only: a suggestion neither reserves nor allocates a shared identity. */
export function suggestRuleKey(name: string, existing: ReadonlySet<string>): string {
  const base =
    name
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '') || 'rule'
  let candidate = base
  let n = 2
  while (existing.has(candidate)) {
    candidate = `${base}-${n}`
    n += 1
  }
  return candidate
}
