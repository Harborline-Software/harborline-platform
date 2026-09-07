import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { place } from '../../projections/blazor/ui/hlp.ui.popover/wwwroot/popover.js'

const table = JSON.parse(readFileSync(resolve(import.meta.dirname, 'placement-v1.json'), 'utf8'))
const row = JSON.parse(readFileSync(resolve(import.meta.dirname, 'fixtures.yaml'), 'utf8'))
  .cases.find(candidate => candidate.id === 'popover.placement')
if (!row) throw new Error('missing-neutral-fixture: popover.placement')
if (row.input.fixture !== 'placement-v1.json') throw new Error(`unexpected-placement-fixture: ${row.input.fixture}`)

// The Blazor lane's half of popover.placement, run here because bUnit cannot execute the module's
// JavaScript. The React lane replays the same table through its own place() in
// Popover.conformance.test.tsx; a lane that clamps without flipping is red on 12 of the 24 rows.
describe('Popover Blazor JavaScript placement (conformance/hlp.ui.popover/placement-v1.json)', () => {
  it('resolves the declared side and offsets for every row', () => {
    expect(table.cases).toHaveLength(row.expected.cases)
    expect(table.cases.filter(entry => entry.expected.side !== entry.side)).toHaveLength(row.expected.flippedCases)
    for (const entry of table.cases) {
      const anchor = table.anchors[entry.anchor]
      if (!anchor) throw new Error(`missing-placement-anchor: ${entry.anchor}`)
      const resolved = place(anchor, table.content, entry.side, entry.align, table.sideOffset, entry.direction, table.viewport)
      expect(resolved, `${entry.anchor} ${entry.side} ${entry.align}`).toEqual(entry.expected)
    }
  })
})
