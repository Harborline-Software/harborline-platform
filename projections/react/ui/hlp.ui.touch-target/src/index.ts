/** Grow the control box to a 44-by-44 minimum without sizing its glyph. */
export const touchTarget = 'min-h-11 min-w-11'

/**
 * Center a transparent 44-by-44 pseudo-element without changing the host's
 * positioning mode. The host must already provide a positioning context.
 */
export const touchTargetPseudoOverlay =
  "before:content-[''] before:absolute before:left-1/2 before:top-1/2 " +
  'before:h-11 before:w-11 before:-translate-x-1/2 before:-translate-y-1/2'

/** Add a relative positioning context to the centered pseudo-element. */
export const touchTargetPseudo = `relative ${touchTargetPseudoOverlay}`
