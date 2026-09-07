// Re-measure gallery/element-parity-known-divergences.json from a measure run.
//
// The register records the element-level lane divergences that exist ON MAIN TODAY, and main moves
// under it: a landing that fixes a divergence turns its row red (correctly) and a landing that
// changes markup adds rows. Re-measuring it by hand is a diff of hundreds of rows, so it is one
// command instead: run the gallery gate with HARBORLINE_GALLERY_ELEMENT_MEASURE set, then point this
// at the file it wrote. Ticket and note survive for every row that still differs; rows that no
// longer differ are dropped and new rows arrive owned by --ticket.
import { readFileSync, writeFileSync } from 'node:fs'
import { arch, platform } from 'node:os'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const measurePath = process.argv[2]
if (!measurePath) throw new Error('usage: node tooling/rebuild-element-parity-register.mjs <measure-file> [--ticket 147]')
const ticketIndex = process.argv.indexOf('--ticket')
const defaultTicket = ticketIndex === -1 ? '147' : process.argv[ticketIndex + 1]
const registerPath = resolve(root, 'gallery/element-parity-known-divergences.json')
const register = JSON.parse(readFileSync(registerPath, 'utf8'))
// The same naming gallery.spec.ts derives the running host from. Rows scoped to a DIFFERENT host
// were not measured by this run, so they survive it untouched; re-measuring on Windows must not
// silently delete a mac-only row.
const hostKey = platform() === 'win32' ? `windows-11-${arch()}`
  : platform() === 'darwin' ? (arch() === 'x64' ? 'macos-x64-intel' : `macos-${arch()}`)
  : `${platform()}-${arch()}`
const appliesHere = row => !row.host || row.host === hostKey

const previous = new Map(register.divergences.map(row => [`${row.scenarioId}\t${row.path}\t${row.property}`, row]))
// A failed attempt is retried (`retries: 1`) and appends its census again, so the same key can
// appear twice in one measure file. The LAST occurrence is the one that ran to completion.
const byKey = new Map()
const seenScenarios = new Set()
for (const line of readFileSync(measurePath, 'utf8').split('\n')) {
  if (!line.trim()) continue
  const [scenarioId, path, property, , hidden] = line.split('\t')
  seenScenarios.add(scenarioId)
  if (property === '-') continue
  const key = `${scenarioId}\t${path}\t${property}`
  const before = previous.get(key)
  const row = {
    scenarioId,
    path,
    property,
    ticket: before?.ticket ?? defaultTicket,
    note: before?.note ?? 'first seen by this probe; not yet diagnosed',
  }
  // A row already scoped to THIS host keeps its scope. A row scoped to another host that also
  // differs here differs on both, so it loses the scope and applies everywhere.
  if (before?.host === hostKey) row.host = hostKey
  if (property === 'element') {
    const [react, blazor] = (hidden ?? '').split(',')
    row.hiddenDescendants = { react: Number(react ?? 0), blazor: Number(blazor ?? 0) }
  }
  byKey.set(key, row)
}
const rows = [...byKey.values()]

const moduleOf = id => id.split('.').slice(0, -1).join('.')
const delta = {}
const measured = new Set(rows.map(row => `${row.scenarioId}\t${row.path}\t${row.property}`))
for (const row of rows) {
  const bucket = (delta[moduleOf(row.scenarioId)] ??= { added: 0, kept: 0, removed: 0 })
  if (previous.has(`${row.scenarioId}\t${row.path}\t${row.property}`)) bucket.kept += 1
  else bucket.added += 1
}
for (const [key, row] of previous) {
  if (measured.has(key)) continue
  if (!appliesHere(row)) { rows.push(row); continue }
  // A scenario the measure run did not visit was not measured, so its rows are not evidence of a fix.
  if (!seenScenarios.has(row.scenarioId)) throw new Error(`measure file did not visit ${row.scenarioId}, which the register lists; re-measure every compared scenario`)
  ;(delta[moduleOf(row.scenarioId)] ??= { added: 0, kept: 0, removed: 0 }).removed += 1
}

rows.sort((left, right) => `${left.scenarioId}\t${left.path}\t${left.property}`.localeCompare(`${right.scenarioId}\t${right.path}\t${right.property}`))
register.divergences = rows
writeFileSync(registerPath, `${JSON.stringify(register, null, 2)}\n`)
process.stdout.write(`${JSON.stringify({ rows: rows.length, scenarios: new Set(rows.map(row => row.scenarioId)).size, byModule: delta }, null, 2)}\n`)
