import { DataGrid, type DataGridColumnDef } from '@harborline-platform/hlp.ui.data-grid'
import type { ViewDefinitionField, ViewRuntimeProps, ViewRuntimeRow } from './ViewRuntime.types'

const GRID_KIND = 'views.entity-list/grid'

function columns(fields: readonly ViewDefinitionField[]): readonly DataGridColumnDef<ViewRuntimeRow>[] {
  return fields.map((field, index) => ({ id: field.id, field: row => {
    const value = row[field.id]
    return value == null ? '' : String(value)
  }, header: field.label ?? field.id, removalPriority: fields.length - index }))
}

export function ViewRuntime({ plan, rows, accessibleName = 'View results', empty }: ViewRuntimeProps) {
  if (plan.definitionKind !== 'ViewDefinition' || plan.bindings.viewKind !== GRID_KIND) return null
  const fields = plan.bindings.parameters?.fields
  if (!fields) return null
  const source = JSON.stringify({ definitionId: plan.definitionId, definitionVersion: plan.definitionVersion, packKey: plan.packKey })
  return <div className="hl-view-runtime" data-definition-source={source} title={source === undefined ? undefined : `Definition source: ${source}`}>
    <DataGrid accessibleName={accessibleName} columns={columns(fields)} empty={empty} getRowId={row => row.id} rows={rows} />
  </div>
}
