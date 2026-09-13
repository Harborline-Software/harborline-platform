import { DataGrid, type DataGridColumnDef } from '@harborline-platform/hlp.ui.data-grid'
import type { ViewDefinitionField, ViewRuntimeProps, ViewRuntimeRow } from './ViewRuntime.types'

const GRID_KIND = 'views.entity-list/grid'

function columns(fields: readonly ViewDefinitionField[]): readonly DataGridColumnDef<ViewRuntimeRow>[] {
  return fields.map((field, index) => ({ id: field.id, field: row => {
    const value = row[field.id]
    return value == null ? '' : String(value)
  }, header: field.label ?? field.id, removalPriority: fields.length - index }))
}

function definitionSource(definition: ViewRuntimeProps['definition']): string | undefined {
  if (!definition.packKey?.trim()) return undefined
  return JSON.stringify({ definitionId: definition.id, definitionVersion: definition.version, packKey: definition.packKey })
}

export function ViewRuntime({ definition, rows, accessibleName = 'View results', empty }: ViewRuntimeProps) {
  if (definition.kind !== GRID_KIND) return null
  const source = definitionSource(definition)
  return <div className="hl-view-runtime" data-definition-source={source} title={source === undefined ? undefined : `Definition source: ${source}`}>
    <DataGrid accessibleName={accessibleName} columns={columns(definition.body.fields)} empty={empty} getRowId={row => row.id} rows={rows} />
  </div>
}
