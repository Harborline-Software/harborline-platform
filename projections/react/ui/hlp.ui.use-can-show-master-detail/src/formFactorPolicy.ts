/**
 * The one master-detail rail decider (ticket 154): the capability policy and the
 * detail panel's docked-versus-modal gate both derive from this single query, so the
 * same viewport can never produce two answers.
 */
export const MASTER_DETAIL_RAIL_QUERY = '(min-width: 768px) and (min-height: 600px)'

export const FORM_FACTOR_QUERIES = {
  phoneWidth: '(max-width: 767px)',
  desktopWidth: '(min-width: 1280px)',
  landscape: '(orientation: landscape)',
  shortHeight: '(max-height: 500px)',
  anyCoarse: '(any-pointer: coarse)',
  anyFine: '(any-pointer: fine)',
  hover: '(hover: hover)',
  splitBuilderPanes: '(min-width: 1024px)',
  masterDetailRail: MASTER_DETAIL_RAIL_QUERY,
} as const

export type FormFactorMode = 'phone' | 'tablet' | 'desktop'
export type Orientation = 'portrait' | 'landscape'
export type HeightClass = 'short' | 'tall'

export interface MatchedFormFactorSignals {
  phoneWidth: boolean
  desktopWidth: boolean
  landscape: boolean
  shortHeight: boolean
  anyCoarse: boolean
  anyFine: boolean
  hover: boolean
  masterDetailRail: boolean
}

export interface ResolvedFormFactor {
  mode: FormFactorMode
  orientation: Orientation
  heightClass: HeightClass
  canShowMasterDetail: boolean
  touchSizing: boolean
  showHoverAffordance: boolean
}

export function resolveFormFactor(signals: Readonly<MatchedFormFactorSignals>): ResolvedFormFactor {
  const shortLandscape = signals.landscape && signals.shortHeight
  const mode: FormFactorMode = signals.phoneWidth || shortLandscape
    ? 'phone'
    : signals.desktopWidth
      ? 'desktop'
      : 'tablet'

  return {
    mode,
    orientation: signals.landscape ? 'landscape' : 'portrait',
    heightClass: signals.shortHeight ? 'short' : 'tall',
    // Fail closed: master-detail needs the rail viewport the detail panel itself docks at,
    // not merely a non-phone mode (768x550 is tablet mode yet below the rail's 600px floor).
    canShowMasterDetail: mode !== 'phone' && signals.masterDetailRail,
    touchSizing: mode === 'phone' || signals.anyCoarse,
    showHoverAffordance: signals.hover || signals.anyFine,
  }
}
