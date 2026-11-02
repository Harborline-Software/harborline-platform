// One file per module for the two registries every module change used to edit (HLP-0009).
//
// `catalog/modules.yaml` and `docs/provenance/source-map.yaml` were each one document holding a row
// per module. Nearly every module change had to edit both -- a catalog `interface.revision` bump and
// the provenance `contentHashes` of the files it touched -- so pull requests for unrelated modules
// changed the same two files, conflicted on rebase, and held each other in the slice picker's overlap
// check. Measured 2026-11-01 over the open pull requests: 16 changed the source map and 8 the catalog
// in their own diffs, while no other path was changed by more than 6.
//
// Each module's rows now live in their own file:
//   catalog/modules/<moduleId>.json          {moduleId, module, seams?}
//   docs/provenance/records/<moduleId>.json  the provenance record itself (it already names its module)
// and the two original paths keep only what is not per-module: identity, schema version and the
// directory the rows live in. No combined copy is checked in, so there is nothing to regenerate and
// nothing for two branches to fight over. Readers call `loadModuleCatalog` / `loadProvenance`, which
// return the shape the single documents had, with modules, seams and records sorted by id.
//
// Every row file is canonical JSON (two-space indent, trailing newline), so a revision bump stays a
// one-line diff and `rowFileErrors` can refuse a hand edit that reflows a file.
import {existsSync, readFileSync, readdirSync, writeFileSync, mkdirSync} from 'node:fs'
import {resolve} from 'node:path'

export const CATALOG_PATH = 'catalog/modules.yaml'
export const CATALOG_DIRECTORY = 'catalog/modules'
export const PROVENANCE_PATH = 'docs/provenance/source-map.yaml'
export const PROVENANCE_DIRECTORY = 'docs/provenance/records'
export const CATALOG_SCHEMA_VERSION = 2
export const PROVENANCE_SCHEMA_VERSION = 3

const MODULE_ID = /^hlp\.[a-z0-9]+(?:[.-][a-z0-9]+)*$/
const byId = (a, b) => (a < b ? -1 : a > b ? 1 : 0)

export const canonicalJson = value => `${JSON.stringify(value, null, 2)}\n`
export const moduleFilePath = moduleId => `${CATALOG_DIRECTORY}/${moduleId}.json`
export const provenanceFilePath = moduleId => `${PROVENANCE_DIRECTORY}/${moduleId}.json`

const readJson = path => JSON.parse(readFileSync(path, 'utf8'))

function rowFiles(root, directory) {
  const absolute = resolve(root, directory)
  if (!existsSync(absolute)) return []
  return readdirSync(absolute).filter(name => name.endsWith('.json')).sort(byId)
    .map(name => ({name, path: `${directory}/${name}`, value: readJson(resolve(absolute, name))}))
}

// The catalog as the single document used to read: {schemaVersion, repository, modules, seams}.
// A seam is stored with the module that owns its interface, so `interfaceOwner` is supplied here.
export function loadModuleCatalog(root) {
  const header = readJson(resolve(root, CATALOG_PATH))
  const modules = {...(header.modules ?? {})}
  const seams = {...(header.seams ?? {})}
  for (const {value} of rowFiles(root, header.moduleDirectory ?? CATALOG_DIRECTORY)) {
    modules[value.moduleId] = value.module
    for (const [seamId, seam] of Object.entries(value.seams ?? {})) seams[seamId] = {interfaceOwner: value.moduleId, ...seam}
  }
  const {moduleDirectory, ...identity} = header
  return {
    ...identity,
    modules: Object.fromEntries(Object.keys(modules).sort(byId).map(id => [id, modules[id]])),
    seams: Object.fromEntries(Object.keys(seams).sort(byId).map(id => [id, seams[id]])),
  }
}

// The provenance map as the single document used to read, records sorted by module.
export function loadProvenance(root) {
  const header = readJson(resolve(root, PROVENANCE_PATH))
  const records = [...(header.records ?? [])]
  for (const {value} of rowFiles(root, header.recordDirectory ?? PROVENANCE_DIRECTORY)) records.push(value)
  const {recordDirectory, ...rest} = header
  return {...rest, records: records.sort((a, b) => byId(a.moduleId, b.moduleId))}
}

// Splits a single-document catalog (schemaVersion 1) into its header and one row file per module.
export function splitCatalog(document) {
  const {modules = {}, seams = {}, schemaVersion, ...identity} = document
  const owned = new Map()
  for (const [seamId, {interfaceOwner, ...seam}] of Object.entries(seams)) {
    if (!Object.hasOwn(modules, interfaceOwner)) throw new Error(`seam ${seamId} names unknown interface owner ${interfaceOwner}`)
    if (!owned.has(interfaceOwner)) owned.set(interfaceOwner, {})
    owned.get(interfaceOwner)[seamId] = seam
  }
  const files = new Map(Object.keys(modules).sort(byId).map(moduleId => [moduleId,
    {moduleId, module: modules[moduleId], ...(owned.has(moduleId) ? {seams: owned.get(moduleId)} : {})}]))
  return {header: {schemaVersion: CATALOG_SCHEMA_VERSION, ...identity, moduleDirectory: CATALOG_DIRECTORY}, files}
}

// Splits a single-document provenance map (schemaVersion 2) into its header and one record per module.
export function splitProvenance(document) {
  const {records = [], schemaVersion, ...rest} = document
  const files = new Map()
  for (const record of records) {
    if (files.has(record.moduleId)) throw new Error(`two provenance records for ${record.moduleId}`)
    files.set(record.moduleId, record)
  }
  return {header: {schemaVersion: PROVENANCE_SCHEMA_VERSION, ...rest, recordDirectory: PROVENANCE_DIRECTORY}, files}
}

export function writeModuleFile(root, file) {
  mkdirSync(resolve(root, CATALOG_DIRECTORY), {recursive: true})
  writeFileSync(resolve(root, moduleFilePath(file.moduleId)), canonicalJson(file))
}

export function writeProvenanceRecord(root, record) {
  mkdirSync(resolve(root, PROVENANCE_DIRECTORY), {recursive: true})
  writeFileSync(resolve(root, provenanceFilePath(record.moduleId)), canonicalJson(record))
}

// What the validator refuses: rows back in a header, a file named for a module it does not hold, a
// seam claimed by two modules, and a file that is not in canonical form (which is what keeps a
// one-field change a one-line diff).
export function rowFileErrors(root) {
  const errors = []
  const catalogHeader = readJson(resolve(root, CATALOG_PATH))
  if (catalogHeader.schemaVersion !== CATALOG_SCHEMA_VERSION) errors.push(`${CATALOG_PATH}: schemaVersion must be ${CATALOG_SCHEMA_VERSION}`)
  if (catalogHeader.moduleDirectory !== CATALOG_DIRECTORY) errors.push(`${CATALOG_PATH}: moduleDirectory must be ${CATALOG_DIRECTORY}`)
  for (const key of ['modules', 'seams']) {
    if (Object.hasOwn(catalogHeader, key)) errors.push(`${CATALOG_PATH}: ${key} belong in ${CATALOG_DIRECTORY}/<moduleId>.json, not in the header`)
  }
  const provenanceHeader = readJson(resolve(root, PROVENANCE_PATH))
  if (provenanceHeader.schemaVersion !== PROVENANCE_SCHEMA_VERSION) errors.push(`${PROVENANCE_PATH}: schemaVersion must be ${PROVENANCE_SCHEMA_VERSION}`)
  if (provenanceHeader.recordDirectory !== PROVENANCE_DIRECTORY) errors.push(`${PROVENANCE_PATH}: recordDirectory must be ${PROVENANCE_DIRECTORY}`)
  if (Object.hasOwn(provenanceHeader, 'records')) errors.push(`${PROVENANCE_PATH}: records belong in ${PROVENANCE_DIRECTORY}/<moduleId>.json, not in the header`)

  const seamOwners = new Map()
  for (const [directory, label] of [[CATALOG_DIRECTORY, 'catalog'], [PROVENANCE_DIRECTORY, 'provenance']]) {
    const absolute = resolve(root, directory)
    if (!existsSync(absolute)) { errors.push(`${directory}: missing`); continue }
    for (const name of readdirSync(absolute).sort(byId)) {
      const path = `${directory}/${name}`
      if (!name.endsWith('.json')) { errors.push(`${path}: only <moduleId>.json files belong here`); continue }
      const text = readFileSync(resolve(absolute, name), 'utf8')
      let value
      try { value = JSON.parse(text) } catch (cause) { errors.push(`${path}: not JSON (${cause.message})`); continue }
      const moduleId = name.slice(0, -'.json'.length)
      if (!MODULE_ID.test(moduleId)) errors.push(`${path}: not a module id`)
      if (value.moduleId !== moduleId) errors.push(`${path}: holds ${JSON.stringify(value.moduleId)}, not ${moduleId}`)
      if (text !== canonicalJson(value)) errors.push(`${path}: not canonical JSON; run node tooling/module-catalog.mjs --format`)
      if (label === 'catalog') {
        const extra = Object.keys(value).filter(key => !['moduleId', 'module', 'seams'].includes(key))
        if (extra.length) errors.push(`${path}: unexpected top-level ${extra.join(', ')}`)
        if (!value.module || typeof value.module !== 'object') errors.push(`${path}: module row missing`)
        for (const seamId of Object.keys(value.seams ?? {})) {
          if (Object.hasOwn(value.seams[seamId], 'interfaceOwner')) errors.push(`${path}: seam ${seamId} must not restate interfaceOwner; the file names it`)
          if (seamOwners.has(seamId)) errors.push(`${path}: seam ${seamId} is also declared by ${seamOwners.get(seamId)}`)
          else seamOwners.set(seamId, moduleId)
        }
      }
    }
  }
  return errors
}

// `--format` rewrites every row file in canonical form; `--check` reports rowFileErrors.
if (import.meta.main) {
  const root = process.cwd()
  const argv = process.argv.slice(2)
  if (argv.includes('--format')) {
    for (const directory of [CATALOG_DIRECTORY, PROVENANCE_DIRECTORY]) {
      for (const {path, value} of rowFiles(root, directory)) writeFileSync(resolve(root, path), canonicalJson(value))
    }
  }
  const errors = rowFileErrors(root)
  if (errors.length) {
    process.stderr.write(`${errors.join('\n')}\n`)
    process.exit(1)
  }
  process.stdout.write('module catalog and provenance row files OK\n')
}
