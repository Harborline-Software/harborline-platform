/**
 * FR-3: shared size-vocab normalization helper.
 *
 * Canonical vocabulary: 'sm' | 'md' | 'lg'
 * Deprecated aliases:   'small' | 'medium' | 'large'
 *
 * All components that carry an appearance-size prop MUST funnel through
 * normalizeSize() so the alias mapping is consistent fleet-wide and a later
 * wave can retire the deprecated tokens in one place.
 *
 * NOTE: TextArea.size ('none'|'vertical'|'horizontal'|'both') is the CSS
 * resize axis — NOT an appearance-size prop and MUST NOT pass through here.
 */
export type CanonicalSize = 'sm' | 'md' | 'lg'
export type SizeAlias = CanonicalSize | 'small' | 'medium' | 'large'

const sizeAliasMap: Record<SizeAlias, CanonicalSize> = {
  sm: 'sm',
  md: 'md',
  lg: 'lg',
  small: 'sm',
  medium: 'md',
  large: 'lg',
}

/** Normalize a canonical or deprecated size alias to the canonical token. */
export function normalizeSize(size: SizeAlias): CanonicalSize {
  return sizeAliasMap[size] ?? 'md'
}
