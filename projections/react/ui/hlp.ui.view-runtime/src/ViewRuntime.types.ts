export interface ViewDefinitionField { readonly id: string; readonly label?: string }
export interface ViewRenderPlanParameters { readonly fields?: readonly ViewDefinitionField[] }
export interface ViewRenderPlanBindings { readonly viewKind?: string; readonly parameters?: ViewRenderPlanParameters }
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
}
