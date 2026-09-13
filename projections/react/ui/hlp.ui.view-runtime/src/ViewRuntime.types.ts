export interface ViewDefinitionField { readonly id: string; readonly label?: string }
export interface ViewDefinitionBody { readonly fields: readonly ViewDefinitionField[] }
export interface ViewDefinition { readonly id: string; readonly kind: string; readonly version: string; readonly packKey?: string; readonly body: ViewDefinitionBody }
export interface ViewRuntimeRow { readonly id: string; readonly [field: string]: unknown }
export interface ViewRuntimeProps {
  readonly definition: ViewDefinition
  readonly rows: readonly ViewRuntimeRow[]
  readonly accessibleName?: string
  readonly empty?: string
}
