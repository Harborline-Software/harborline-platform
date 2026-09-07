// DETAIL_PANEL_RAIL_QUERY is gone (ticket 154 review): the panel calls the capability policy
// (useCanShowMasterDetail) instead of re-evaluating a query string. Consumers who need the
// query identity import MASTER_DETAIL_RAIL_QUERY from hlp.ui.use-can-show-master-detail.
export { DetailPanel } from './DetailPanel'
export type { DetailPanelFacet, DetailPanelProps, DetailPanelRelation, DetailPanelSubject } from './DetailPanel'
