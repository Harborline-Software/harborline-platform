import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import React from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { ActivityLog, Page } from '@harborline-software/ui-react'

const corpus = JSON.parse(readFileSync(new URL('../corpus/aggregates-vertical-cases.json', import.meta.url), 'utf8'))
assert.equal(corpus.portedCases.length, 7)
assert.equal(corpus.authoredCases.length, 4)
assert.ok(corpus.authoredCases.every(row => row.authored === true))
assert.equal(corpus.carriedUiCases.length, 20)
assert.ok(corpus.carriedUiCases.every(row => row.carried === true))
assert.equal(corpus.crossLanePairs.length, 4)

// The engine inserts each draft at the front; mirror that package behavior from the same rows.
const entries = corpus.fixtureEntries.entriesInAppendOrder.toReversed().map(row => ({
  id: row.id,
  actor: row.displayName,
  action: row.action,
  detail: row.detail ?? undefined,
  timestamp: row.timestamp,
  category: row.category,
}))
const countStatus = `${entries.length} events`
const composed = renderToStaticMarkup(
  React.createElement(Page, {
    title: 'Activity',
    subtitle: 'Attributed history for this session',
    actions: React.createElement('span', { role: 'status' }, countStatus),
  }, React.createElement(ActivityLog, {
    entries,
    layout: 'table',
    label: 'Activity log',
    maxVisible: 1,
    onShowMore: () => {},
  })),
)
assert.match(composed, /hl-page/)
assert.match(composed, /role="status">2 events/)
assert.match(composed, /hl-activity-log/)
assert.match(composed, /data-hl-layout="table"/)
assert.match(composed, /act:result:j2|Failed/)
assert.doesNotMatch(composed, /TTS complete/)
assert.match(composed, />Show more</)

const list = renderToStaticMarkup(React.createElement(ActivityLog, {
  entries, layout: 'list', label: 'Activity log',
}))
assert.match(list, /data-hl-layout="list"/)
assert.match(list, /Failed/)
assert.match(list, /TTS complete/)
assert.match(list, /Voice: Samantha/)
assert.equal((list.match(/<time/g) ?? []).length, entries.length)

const empty = renderToStaticMarkup(React.createElement(ActivityLog, {
  entries: [], layout: 'table', label: 'Activity log', emptyLabel: 'No activity yet',
}))
assert.match(empty, />No activity yet</)

const projection = {
  orderedIds: entries.map(entry => entry.id),
  displayNames: entries.map(entry => entry.actor),
  categories: entries.map(entry => entry.category),
  count: entries.length,
}
for (const pair of corpus.crossLanePairs) {
  const actual = pair.pair === 'ordered-ids' ? projection.orderedIds
    : pair.pair === 'attribution-display-names' ? projection.displayNames
      : pair.pair === 'categories' ? projection.categories
        : pair.pair === 'count' ? projection.count
          : undefined
  assert.deepEqual(actual, pair.expected, `cross-lane pair ${pair.pair}`)
}
process.stdout.write(`CROSS_LANE_TIMELINE:${JSON.stringify(projection)}\n`)
process.stdout.write(`AGGREGATES_RENDERER_PASS:${JSON.stringify({ carried: corpus.carriedUiCases.length, renderedEntries: entries.length, emptyState: true, showMore: true })}\n`)
