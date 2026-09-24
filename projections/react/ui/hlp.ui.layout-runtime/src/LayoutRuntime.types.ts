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

/** The five binding kinds a block may carry (DES-0052 layout-ck-21 to layout-ck-25). */
export type LayoutBindingKind = 'record_field' | 'query' | 'measure' | 'template' | 'static'

/**
 * One authored binding: the kind, and the name it resolves by. A binding whose `name` is
 * empty is the `needs a binding` state — the block is preserved and rebound one at a time
 * rather than deleted (layout-auth-31).
 */
export interface LayoutAuthoringBinding {
  readonly kind: LayoutBindingKind
  readonly name: string
}

export interface LayoutAuthoringBlock {
  readonly id: string
  readonly kind: string
  readonly binding?: LayoutAuthoringBinding
  /** The block this one is authored inside; absent means the surface root. */
  readonly parentId?: string
  /** layout-auth-18: the block repeats its children once per row of its collection binding. */
  readonly repeating?: boolean
  /** layout-auth-19: the declared Records relationship this block observes; only the key is stored. */
  readonly relatedRelationship?: string
  /** layout-auth-20: the block's guard, a Rules expression the shared engine evaluates fail-closed. */
  readonly showWhen?: string
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
/** One name a binding picker can offer for its kind. */
export interface LayoutBindableName { readonly id: string; readonly label: string }

export interface LayoutAuthoringCatalogue {
  readonly blockKinds: readonly { readonly id: string; readonly label: string }[]
  readonly zones: readonly string[]
  /** The names offered per binding kind; `static` is authored on the block, so it is absent. */
  readonly bindables?: Partial<Record<Exclude<LayoutBindingKind, 'static'>, readonly LayoutBindableName[]>>
  readonly pageLayouts?: readonly { readonly id: string; readonly label: string }[]
  readonly pageMasters?: readonly { readonly id: string; readonly label: string }[]
  readonly staticRegions?: readonly string[]
  readonly helmWidgets?: readonly { readonly id: string; readonly label: string }[]
  /** The Records relationships declared on the surface's record type, by key (layout-auth-19). */
  readonly relationships?: readonly LayoutBindableName[]
}
export interface LayoutAuthoringEditorProps { readonly value: LayoutAuthoringDraft; readonly catalogue: LayoutAuthoringCatalogue; readonly onChange: (value: LayoutAuthoringDraft) => void }
