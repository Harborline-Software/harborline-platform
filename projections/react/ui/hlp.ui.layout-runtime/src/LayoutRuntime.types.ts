import type { RuleDefinitionExpression, RuleDefinitionValueType } from '@harborline-software/rule-authoring'

export type LayoutMedium = 'screen' | 'page'
export type LayoutContainerToken = 'sm' | 'md' | 'lg'
export type LayoutIntent = 'capture' | 'observe' | 'issue'
export type LayoutContainerFlow = 'stack' | 'flow' | 'areas'
export type LayoutAlignment = 'start' | 'center' | 'end' | 'stretch'
export type LayoutSizing = 'hug' | 'fill' | 'fixed'

export interface LayoutRuntimeDiagnostic { readonly code: string; readonly pointer: string }
export interface LayoutRuntimeBlock { readonly id: string; readonly kind: string; readonly depth: number; readonly zone?: string }
/** DES-0052 C1, layout-eng-26: the authority the platform returned with the surface. The lane derives nothing from it. */
export interface LayoutRuntimeAuthority { readonly canSubmit: boolean }
export interface LayoutRuntimePlan {
  readonly definitionId: string
  readonly definitionVersionId: string
  readonly medium: LayoutMedium
  readonly flow: readonly LayoutRuntimeBlock[]
  readonly staticRegions: readonly LayoutRuntimeBlock[]
  readonly diagnostics?: readonly LayoutRuntimeDiagnostic[]
  /** Absent, the surface is read-only: no authority, no submit. */
  readonly authority?: LayoutRuntimeAuthority
}
/** `onSubmit` hands the submit to the host, which re-asks the platform's gate before any write. */
export interface LayoutRuntimeProps { readonly plan: LayoutRuntimePlan; readonly onSubmit?: () => void }

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

/**
 * What a capture block narrows (layout-ck-30): it may add a requirement and name registered
 * validation rules (layout-auth-21), never remove what Records declared.
 */
export interface LayoutAuthoringCapture {
  readonly required?: boolean
  readonly validationRules?: readonly string[]
  /** layout-auth-22: the prompt this surface shows for the field, in its own context only. */
  readonly promptOverride?: string
  /** layout-bound-3: the registered field control this capture field uses; absent is the runtime's choice. */
  readonly control?: string
}

/** A Rules named predicate's exact pin (rules-ck-22): nothing floats. */
export interface LayoutPredicatePin { readonly name: string; readonly version: string; readonly digest: string }

/**
 * layout-ck-29: a guard holds exactly one of a Rules expression, compiled at publish, or a named
 * predicate by exact pin, resolved from the definition's pinned closure. Both fail closed.
 */
export type LayoutAuthoringShowWhen =
  | { readonly expression: string; readonly predicate?: never }
  | { readonly predicate: LayoutPredicatePin; readonly expression?: never }

export interface LayoutAuthoringBlock {
  readonly id: string
  readonly kind: string
  readonly binding?: LayoutAuthoringBinding
  /** The block this one is authored inside; absent means the surface root. */
  readonly parentId?: string
  /** layout-auth-18: the block repeats its children once per row of its collection binding. */
  readonly repeating?: boolean
  /** The flow this block arranges its children in; admission requires one on a repeating block or any parent (T-724 ruling 41). */
  readonly container?: LayoutContainerFlow
  /** layout-auth-19: the declared Records relationship this block observes; only the key is stored. */
  readonly relatedRelationship?: string
  /** layout-ck-29, layout-auth-20: the block's guard, holding exactly one form; absent, the block always shows. */
  readonly showWhen?: LayoutAuthoringShowWhen
  /** The guided expression `showWhen` was lowered from; absent when the guard was written as raw text (T-724 ruling 39). */
  readonly showWhenGuide?: RuleDefinitionExpression
  /** The capture properties a capture block narrows with (layout-ck-30). */
  readonly capture?: LayoutAuthoringCapture
  /** layout-auth-33: the selection this block opens with. Authored; the live selection is never stored. */
  readonly defaultSelection?: string
  /** layout-auth-34: the ids of the other blocks on this surface this block's selection filters. */
  readonly filterTargets?: readonly string[]
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
  /** layout-auth-35: the released surfaces a reader may drill through to from this one. */
  readonly drillThroughTargets?: readonly string[]
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
  /** The record fields Records declares required; a capture block cannot drop them (layout-auth-21). */
  readonly requiredFields?: readonly string[]
  /** The registered validation rules a capture block may name (layout-auth-21, layout-bound-8). */
  readonly validationRules?: readonly LayoutBindableName[]
  /** The released surfaces a drill-through may name (layout-auth-35). */
  readonly drillTargets?: readonly LayoutBindableName[]
  /** The field controls the host registers for capture fields (layout-bound-3). */
  readonly fieldControls?: readonly LayoutBindableName[]
  /** The record fields whose value domain picks their editor; they take no authored control (layout-bound-10). */
  readonly valueDomainFields?: readonly string[]
  /** The references a show_when guard may read, offered by the guided expression editor (layout-auth-20). */
  readonly guardReferences?: readonly { readonly id: string; readonly label: string; readonly valueType: RuleDefinitionValueType }[]
  /** The named predicates a show_when guard may cite, each by its exact pin (layout-ck-29). */
  readonly predicates?: readonly { readonly label: string; readonly pin: LayoutPredicatePin }[]
}
export interface LayoutAuthoringEditorProps { readonly value: LayoutAuthoringDraft; readonly catalogue: LayoutAuthoringCatalogue; readonly onChange: (value: LayoutAuthoringDraft) => void }
