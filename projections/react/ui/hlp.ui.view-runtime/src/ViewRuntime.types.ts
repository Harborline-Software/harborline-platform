export const VIEW_KIND_TABLE = 'layout.table' as const

export interface ViewDefinitionField { readonly id: string; readonly label?: string }
export interface ViewRenderPlanParameters { readonly fields?: readonly ViewDefinitionField[] }
export interface ViewRuntimeAction { readonly id: string; readonly label: string }
export interface ViewRenderPlanBindings { readonly viewKind?: string; readonly parameters?: ViewRenderPlanParameters; readonly actions?: readonly ViewRuntimeAction[] }
export interface ViewRenderPlan {
  readonly definitionHash: string
  readonly definitionId: string
  readonly definitionVersion: string
  readonly packKey: string
  readonly packVersion: string
  readonly definitionKind: string
  readonly bindings: ViewRenderPlanBindings
}
export interface ViewRuntimeRow { readonly id: string; readonly [field: string]: unknown }
export interface ViewRuntimeProps {
  readonly plan: ViewRenderPlan
  readonly rows: readonly ViewRuntimeRow[]
  readonly accessibleName?: string
  readonly empty?: string
  readonly actionsDisabled?: boolean
  readonly onRowActivate?: (rowId: string) => void
  readonly onAction?: (actionId: string) => void
}
