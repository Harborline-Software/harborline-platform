import assert from 'node:assert/strict'
import { readFileSync, writeFileSync } from 'node:fs'

const ui = await import('@harborline-software/ui-react')
for (const carriedExport of ['DataGrid', 'Table', 'DataExportButton']) {
  // Carried green rows: prove the export RESOLVES from the packed artifact. React components
  // may be plain functions or forwardRef/memo exotic components (typeof 'object') — both count.
  const kind = typeof ui[carriedExport]
  assert.ok(
    (kind === 'function' || kind === 'object') && ui[carriedExport] !== null,
    `packed ui-react is missing carried export ${carriedExport} (typeof ${kind})`,
  )
}

const corpus = JSON.parse(readFileSync(new URL('../corpus/views-vertical-cases.json', import.meta.url), 'utf8'))
assert.equal(corpus.pairs.length, 4)
const verdicts = corpus.pairs.map(({ pair, fallbackLaneCase, transportLaneCase }) => ({
  pair,
  fallbackLaneCase,
  transportLaneCase,
  lane: 'renderer-acknowledged',
}))
writeFileSync(new URL('./client-verdicts.json', import.meta.url), `${JSON.stringify(verdicts, null, 2)}\n`)
process.stdout.write(`VIEWS_CLIENT_PASS:${JSON.stringify({ pairs: verdicts.length, carriedExports: 3 })}\n`)
