import type { Meta, StoryObj } from '@storybook/react-vite'
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeaderCell, TableRow } from '@harborline-software/ui-react'

type ScenarioId = 'table.semantic' | 'table.density' | 'table.reflow' | 'table.theme' | 'table.content'
const rows = [['North Pier', 'Active', 'Today'], ['Pump House', 'Review', 'Yesterday'], ['Main Span', 'Active', 'Friday']]
const contentRows = [
  ['Ordnance Survey — Niño Ångström', 'Bay 4 <grid C-7> & 8', '1,284,905', 'Awaiting review'],
  ['Miyazaki — Élodie Brontë', 'Pier 12', '984,321', 'Ready'],
  ['Søren Łukasz', 'Dock 6', '72,440', 'Pending'],
  ['Zoë François', 'Warehouse 9', '6,007', 'Archived'],
]

function TableScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const themed = scenarioId === 'table.theme'
  const pseudo = scenarioId === 'table.reflow'
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={themed ? 'dark' : undefined} dir={themed ? 'rtl' : undefined}>
    <header className="hl-gallery-heading"><h2>{scenarioId === 'table.content' ? 'Content resilience' : scenarioId === 'table.density' ? 'Density' : pseudo ? 'Narrow viewport' : themed ? 'Theme parity' : 'Semantic table'}</h2><p>Native table semantics remain available inside the overflow surface.</p></header>
    <div className="hl-gallery-stage" style={pseudo ? { maxWidth: 360 } : undefined}>{scenarioId === 'table.content'
      ? <Table density="sm"><TableCaption>{'Awaiting third-party structural certification review'}</TableCaption><TableHead><TableRow><TableHeaderCell>Owner</TableHeaderCell><TableHeaderCell>Location</TableHeaderCell><TableHeaderCell>Records</TableHeaderCell><TableHeaderCell>Status</TableHeaderCell></TableRow></TableHead><TableBody>{contentRows.map(row => <TableRow key={row[0]}>{row.map(value => <TableCell key={value}>{value}</TableCell>)}</TableRow>)}</TableBody></Table>
      : <Table density={scenarioId === 'table.density' ? 'sm' : 'md'}><TableCaption>{pseudo ? '⟦ Šţřûçţûřë îñšþëçţîøñ šûmmåřÿ ······ ⟧' : 'Structure inspection summary'}</TableCaption><TableHead><TableRow><TableHeaderCell>Structure</TableHeaderCell><TableHeaderCell>Status</TableHeaderCell><TableHeaderCell>Updated</TableHeaderCell></TableRow></TableHead><TableBody>{rows.map(row => <TableRow key={row[0]}>{row.map(value => <TableCell key={value}>{value}</TableCell>)}</TableRow>)}</TableBody></Table>}</div>
  </section>
}

const meta = { title: 'Platform/Table', component: TableScenario, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof TableScenario>
export default meta
type Story = StoryObj<typeof meta>
export const Semantic: Story = { name: 'Semantic table', args: { scenarioId: 'table.semantic' } }
export const Density: Story = { args: { scenarioId: 'table.density' } }
export const Reflow: Story = { name: 'Narrow viewport', args: { scenarioId: 'table.reflow' } }
export const Theme: Story = { name: 'Theme parity', args: { scenarioId: 'table.theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'table.content' } }
