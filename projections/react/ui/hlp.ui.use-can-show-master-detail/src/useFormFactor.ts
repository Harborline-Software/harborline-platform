import { useMediaQuery } from '@harborline-platform/hlp.ui.use-media-query'

import {
  FORM_FACTOR_QUERIES,
  resolveFormFactor,
  type ResolvedFormFactor,
} from './formFactorPolicy'

export interface FormFactorState {
  mode: ResolvedFormFactor['mode']
  orientation: ResolvedFormFactor['orientation']
  heightClass: ResolvedFormFactor['heightClass']
}

function useResolvedFormFactor(): ResolvedFormFactor {
  return resolveFormFactor({
    phoneWidth: useMediaQuery(FORM_FACTOR_QUERIES.phoneWidth),
    desktopWidth: useMediaQuery(FORM_FACTOR_QUERIES.desktopWidth),
    landscape: useMediaQuery(FORM_FACTOR_QUERIES.landscape),
    shortHeight: useMediaQuery(FORM_FACTOR_QUERIES.shortHeight),
    anyCoarse: useMediaQuery(FORM_FACTOR_QUERIES.anyCoarse),
    anyFine: useMediaQuery(FORM_FACTOR_QUERIES.anyFine),
    hover: useMediaQuery(FORM_FACTOR_QUERIES.hover),
    masterDetailRail: useMediaQuery(FORM_FACTOR_QUERIES.masterDetailRail),
  })
}

export function useFormFactor(): FormFactorState {
  const { mode, orientation, heightClass } = useResolvedFormFactor()
  return { mode, orientation, heightClass }
}

export function useCanShowMasterDetail(): boolean {
  // Subscribe only to the inputs the decision reads (the useCanSplitBuilderPanes precedent):
  // canShowMasterDetail is (mode !== phone) && rail, and desktop-versus-tablet plus the three
  // pointer/hover signals cannot change that answer — subscribing to all eight re-rendered
  // every consumer (entire admin grids) on orientation/pointer flips. The formula still lives
  // in resolveFormFactor, so the exported semantics are byte-identical to useFormFactor's.
  return resolveFormFactor({
    phoneWidth: useMediaQuery(FORM_FACTOR_QUERIES.phoneWidth),
    desktopWidth: false, // distinguishes desktop from tablet only — both are non-phone
    landscape: useMediaQuery(FORM_FACTOR_QUERIES.landscape),
    shortHeight: useMediaQuery(FORM_FACTOR_QUERIES.shortHeight),
    anyCoarse: false,
    anyFine: false,
    hover: false,
    masterDetailRail: useMediaQuery(FORM_FACTOR_QUERIES.masterDetailRail),
  }).canShowMasterDetail
}

export function useTouchSizing(): boolean {
  return useResolvedFormFactor().touchSizing
}

export function useShowHoverAffordance(): boolean {
  return useResolvedFormFactor().showHoverAffordance
}

export function useCanSplitBuilderPanes(): boolean {
  return useMediaQuery(FORM_FACTOR_QUERIES.splitBuilderPanes)
}
