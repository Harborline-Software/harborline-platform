export type LayoutMedium = 'screen' | 'page'
export type LayoutContainerToken = 'sm' | 'md' | 'lg'
export type LayoutIntent = 'capture' | 'observe' | 'issue'
export type LayoutContainerFlow = 'stack' | 'flow' | 'areas'
export type LayoutAlignment = 'start' | 'center' | 'end' | 'stretch'
export type LayoutSizing = 'hug' | 'fill' | 'fixed'

export interface LayoutRuntimeDiagnostic { readonly code: string; readonly pointer: string }
export interface LayoutRuntimeBlock { readonly id: string; readonly kind: string; readonly depth: number; readonly zone?: string }
export interface LayoutRuntimePlan {
  readonly definitionId: string
  readonly definitionVersionId: string
  readonly medium: LayoutMedium
  readonly flow: readonly LayoutRuntimeBlock[]
  readonly staticRegions: readonly LayoutRuntimeBlock[]
  readonly diagnostics?: readonly LayoutRuntimeDiagnostic[]
}
export interface LayoutRuntimeProps { readonly plan: LayoutRuntimePlan }

export interface LayoutAuthoringBlock {
  readonly id: string
  readonly kind: string
  readonly parentId?: string
  readonly intent?: LayoutIntent
  readonly zone?: string
  readonly width?: LayoutSizing
  readonly height?: LayoutSizing
  readonly justifySelf?: LayoutAlignment
  readonly alignSelf?: LayoutAlignment
  readonly staticRegion?: string
  readonly widgetId?: string
  readonly breakBefore?: boolean
  readonly avoidPageBreak?: boolean
}
export interface LayoutAuthoringPageRun { readonly id: string; readonly pageLayoutId: string; readonly pageMasterId: string }
export interface LayoutAuthoringDraft {
  readonly name: string
  readonly medium: LayoutMedium
  readonly defaultIntent?: LayoutIntent
  readonly containerFlow?: LayoutContainerFlow
  readonly collapseBelow?: LayoutContainerToken
  readonly density?: 'comfortable' | 'compact'
  readonly gap?: number
  readonly blocks: readonly LayoutAuthoringBlock[]
  readonly pageRuns?: readonly LayoutAuthoringPageRun[]
}
export interface LayoutAuthoringCatalogue {
  readonly blockKinds: readonly { readonly id: string; readonly label: string }[]
  readonly zones: readonly string[]
  readonly pageLayouts?: readonly { readonly id: string; readonly label: string }[]
  readonly pageMasters?: readonly { readonly id: string; readonly label: string }[]
  readonly staticRegions?: readonly string[]
  readonly helmWidgets?: readonly { readonly id: string; readonly label: string }[]
}
export interface LayoutAuthoringEditorProps { readonly value: LayoutAuthoringDraft; readonly catalogue: LayoutAuthoringCatalogue; readonly onChange: (value: LayoutAuthoringDraft) => void }
