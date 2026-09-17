import type { ChangeEvent, ReactNode } from 'react'
import type { ViewAuthoringDraft, ViewAuthoringEditorProps, ViewAuthoringOption } from './ViewAuthoringEditor.types'

export function emptyViewAuthoringDraft(): ViewAuthoringDraft {
  return {
    name: '', recordType: '', viewKind: '', columns: [], sorts: [], groupBy: '', filterPredicate: '',
    shapeRoles: { title: '', placedBy: '', groupedBy: '' },
    measure: { name: '', parameters: '' }, widget: { name: '', parameters: '' },
    rowBehavior: { openAction: '', inlineEdit: false }, density: 'standard', ownership: 'public',
  }
}

function Options({ values }: { readonly values: readonly ViewAuthoringOption[] }) {
  return <>{values.map(option => <option key={option.id} value={option.id}>{option.label}</option>)}</>
}

function Section({ name, children }: { readonly name: string; readonly children: ReactNode }) {
  return <fieldset className="hl-view-authoring__section"><legend>{name}</legend>{children}</fieldset>
}

export function ViewAuthoringEditor({ value, catalogue, onChange }: ViewAuthoringEditorProps) {
  const set = <K extends keyof ViewAuthoringDraft>(key: K, next: ViewAuthoringDraft[K]) => onChange({ ...value, [key]: next })
  const selectValue = (event: ChangeEvent<HTMLSelectElement>) => event.currentTarget.value
  const replaceColumn = (index: number, column: ViewAuthoringDraft['columns'][number]) => set('columns', value.columns.map((current, currentIndex) => currentIndex === index ? column : current))
  const replaceSort = (index: number, sort: ViewAuthoringDraft['sorts'][number]) => set('sorts', value.sorts.map((current, currentIndex) => currentIndex === index ? sort : current))
  const move = <T,>(items: readonly T[], index: number, by: number): readonly T[] => {
    const target = index + by
    if (target < 0 || target >= items.length) return items
    const next = [...items]
    ;[next[index], next[target]] = [next[target], next[index]]
    return next
  }

  return <form className="hl-view-authoring" onSubmit={event => event.preventDefault()}>
    <label>View name<input aria-label="View name" value={value.name} onChange={event => set('name', event.currentTarget.value)} /></label>
    <label>Record type<select aria-label="Record type" value={value.recordType} onChange={event => set('recordType', selectValue(event))}><option value="">Choose a record type</option><Options values={catalogue.recordTypes} /></select></label>
    <label>Shape<select aria-label="Shape" value={value.viewKind} onChange={event => set('viewKind', selectValue(event))}><option value="">Choose a shape</option><Options values={catalogue.viewKinds} /></select></label>
    <Section name="Columns">{value.columns.map((column, index) => <div key={index}><select aria-label={`Column ${index + 1}`} value={column.field} onChange={event => replaceColumn(index, { ...column, field: selectValue(event) })}><option value="">Choose a field</option><Options values={catalogue.fields} /></select><button type="button" aria-label={`Move column ${index + 1} up`} onClick={() => set('columns', move(value.columns, index, -1))}>Up</button><button type="button" aria-label={`Move column ${index + 1} down`} onClick={() => set('columns', move(value.columns, index, 1))}>Down</button><button type="button" aria-label={`Remove column ${index + 1}`} onClick={() => set('columns', value.columns.filter((_, current) => current !== index))}>Remove</button></div>)}<button type="button" onClick={() => set('columns', [...value.columns, { field: catalogue.fields[0]?.id ?? '', width: 160, presentation: 'text' }])}>Add column</button></Section>
    <Section name="Column treatment">{value.columns.map((column, index) => <div key={index}><input aria-label={`Column ${index + 1} width`} type="number" min="1" value={column.width} onChange={event => replaceColumn(index, { ...column, width: Number(event.currentTarget.value) })} /><input aria-label={`Column ${index + 1} presentation`} value={column.presentation} onChange={event => replaceColumn(index, { ...column, presentation: event.currentTarget.value })} /></div>)}</Section>
    <Section name="Sort">{value.sorts.map((sort, index) => <div key={index}><select aria-label={`Sort ${index + 1} field`} value={sort.field} onChange={event => replaceSort(index, { ...sort, field: selectValue(event) })}><option value="">Choose a field</option><Options values={catalogue.fields} /></select><select aria-label={`Sort ${index + 1} direction`} value={sort.direction} onChange={event => replaceSort(index, { ...sort, direction: selectValue(event) as typeof sort.direction })}><option value="ascending">Ascending</option><option value="descending">Descending</option></select><button type="button" aria-label={`Move sort ${index + 1} up`} onClick={() => set('sorts', move(value.sorts, index, -1))}>Up</button><button type="button" aria-label={`Move sort ${index + 1} down`} onClick={() => set('sorts', move(value.sorts, index, 1))}>Down</button><button type="button" aria-label={`Remove sort ${index + 1}`} onClick={() => set('sorts', value.sorts.filter((_, current) => current !== index))}>Remove</button></div>)}<button type="button" onClick={() => set('sorts', [...value.sorts, { field: catalogue.fields[0]?.id ?? '', direction: 'ascending' }])}>Add sort</button></Section>
    <label>Group by<select aria-label="Group by" value={value.groupBy} onChange={event => set('groupBy', selectValue(event))}><option value="">No grouping</option><Options values={catalogue.fields} /></select></label>
    <label>Filter predicate<textarea aria-label="Filter predicate" value={value.filterPredicate} onChange={event => set('filterPredicate', event.currentTarget.value)} /></label>
    <Section name="Shape roles">{(['title', 'placedBy', 'groupedBy'] as const).map(role => <select key={role} aria-label={`Shape role ${role}`} value={value.shapeRoles[role]} onChange={event => set('shapeRoles', { ...value.shapeRoles, [role]: selectValue(event) })}><option value="">{role}</option><Options values={catalogue.fields} /></select>)}</Section>
    <Section name="Measured by"><select aria-label="Measured by" value={value.measure.name} onChange={event => set('measure', { ...value.measure, name: selectValue(event) })}><option value="">No measure</option><Options values={catalogue.measures} /></select><input aria-label="Measure parameters" value={value.measure.parameters} onChange={event => set('measure', { ...value.measure, parameters: event.currentTarget.value })} /></Section>
    <Section name="Dashboard widget"><select aria-label="Dashboard widget" value={value.widget.name} onChange={event => set('widget', { ...value.widget, name: selectValue(event) })}><option value="">No widget</option><Options values={catalogue.widgets} /></select><input aria-label="Widget parameters" value={value.widget.parameters} onChange={event => set('widget', { ...value.widget, parameters: event.currentTarget.value })} /></Section>
    <Section name="Row behaviour"><select aria-label="Row open action" value={value.rowBehavior.openAction} onChange={event => set('rowBehavior', { ...value.rowBehavior, openAction: selectValue(event) })}><option value="">No open action</option><Options values={catalogue.rowActions} /></select><label><input aria-label="Inline edit" type="checkbox" checked={value.rowBehavior.inlineEdit} onChange={event => set('rowBehavior', { ...value.rowBehavior, inlineEdit: event.currentTarget.checked })} />Inline edit</label></Section>
    <label>Density<select aria-label="Density" value={value.density} onChange={event => set('density', selectValue(event) as ViewAuthoringDraft['density'])}><option value="compact">Compact</option><option value="standard">Standard</option><option value="spacious">Spacious</option></select></label>
    <label>Who it belongs to<select aria-label="Who it belongs to" value={value.ownership} onChange={event => set('ownership', selectValue(event) as ViewAuthoringDraft['ownership'])}><option value="system">System</option><option value="public">Public</option><option value="personal">Personal</option></select></label>
  </form>
}
