import { DataGrid, type DataGridColumnDef } from '@harborline-platform/hlp.ui.data-grid'
import type { ViewDefinitionField, ViewRuntimeProps, ViewRuntimeRow } from './ViewRuntime.types'

const GRID_KIND = 'views.entity-list/grid'

function columns(fields: readonly ViewDefinitionField[]): readonly DataGridColumnDef<ViewRuntimeRow>[] {
  return fields.map((field, index) => ({ id: field.id, field: row => {
    const value = row[field.id]
    return value == null ? '' : String(value)
  }, header: field.label ?? field.id, removalPriority: fields.length - index }))
}

export function ViewRuntime({ definition, rows, accessibleName = 'View results', empty }: ViewRuntimeProps) {
  if (definition.kind !== GRID_KIND) return null
  return <div className="hl-view-runtime" data-definition-id={definition.id} data-definition-version={definition.version}>
    <DataGrid accessibleName={accessibleName} columns={columns(definition.body.fields)} empty={empty} getRowId={row => row.id} rows={rows} />
  </div>
}
