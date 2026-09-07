import { useMediaQuery } from '@harborline-platform/hlp.ui.use-media-query'

export const BP_PHONE = '(max-width: 767px)'
const BP_RAIL = '(min-width: 768px) and (min-height: 600px)'
export const BP_DOCK = '(min-width: 1280px)'

export function useIsMobile(): boolean {
  return useMediaQuery(BP_PHONE)
}

export function useCanShowRail(): boolean {
  return useMediaQuery(BP_RAIL)
}
