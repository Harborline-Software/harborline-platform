/**
 * Human-facing builder rail text supplied by the host. The contract is
 * intentionally independent of localization runtimes and rendering.
 */
export interface RailLabels {
  lensesHeading: string
  outlineHeading: string
  insert: string
  toggleLens: (lens: string) => string
  activateLens: (lens: string) => string
  passiveCount: (n: number) => string
  source: (tier: string) => string
  locked: string
  unresolvedSource: string
  empty: string
  collapseNode: string
  expandNode: string
  railRegion: string
  viewingLens: (lens: string) => string
  exitLens: string
  lensShortcutHint: (n: number) => string
}

/** Exact English development defaults from the pinned earlier source. */
export const defaultRailLabels: RailLabels = {
  lensesHeading: 'Lenses',
  outlineHeading: 'Outline',
  insert: 'Insert',
  toggleLens: lens => `Toggle ${lens} lens`,
  activateLens: lens => `Show ${lens} lens on the canvas`,
  passiveCount: n => `+${n}`,
  source: tier => tier,
  locked: 'Locked — inherited from a higher tier',
  unresolvedSource: 'source: unknown',
  empty: 'Add your first field — press ⌘K or the + above.',
  collapseNode: 'Collapse',
  expandNode: 'Expand',
  railRegion: 'Layers',
  viewingLens: lens => `Viewing: ${lens}`,
  exitLens: 'Exit lens (back to Layout)',
  lensShortcutHint: n => `press ${n}`,
}
