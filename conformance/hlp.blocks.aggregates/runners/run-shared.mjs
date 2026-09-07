import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(fileURLToPath(new URL('.', import.meta.url)), '../../..')
const corpus = JSON.parse(readFileSync(resolve(root, 'conformance/hlp.blocks.aggregates/corpus/cases.json'), 'utf8'))
const ids = new Set(corpus.cases.map(item => item.id))
const semanticsDerived = corpus.cases.filter(item => item.provenance === 'semantics-derived').length
const authored = corpus.cases.filter(item => item.authored === true).length
if (ids.size !== corpus.cases.length || corpus.cases.length !== 43 || semanticsDerived !== 11 || authored !== 32) {
  throw new Error(`Aggregates corpus mismatch: ${JSON.stringify({ total: corpus.cases.length, unique: ids.size, semanticsDerived, authored })}`)
}
process.stdout.write(`${JSON.stringify({ schemaVersion: 1, moduleId: corpus.moduleId, status: 'PASS', total: corpus.cases.length, semanticsDerived, authored })}\n`)
