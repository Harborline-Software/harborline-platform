#!/usr/bin/env node
// T-585 item 1 / DES-0007 `platform-package-eng-2` and `-eng-3`: every member reaches its runtime
// through the shared builder shell and the shared export path.
//
// The member list is DERIVED, never written here. It comes from `platform-package-ck-6` in the
// released pack -- the register of Workshop members. A fourteenth member added to the seed is
// covered by this check the moment it is exported; a hard-coded list would have stopped covering
// members silently, which is the failure this file exists to prevent.
//
// What a finding names: the member, and what it is missing or carrying of its own. A count or a
// bare boolean would not tell a reader which member broke, and "which one" is the whole question
// when thirteen members share one shell.
import {readFileSync, readdirSync} from 'node:fs'
import {dirname, join, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

export const PACK_PATH = '_shared/packs/platform/platform-pack.export.json'
const REGISTER_ITEM = 'platform-package-ck-6'
const WORKSPACE_ITEM = 'platform-package-ck-5'
const CATALOGUE_VIEW_ITEM = 'platform-package-ck-7'
// DES-0052 layout-eng-24: the one released definition of the Workshop editor destinations (T-584).
export const NAVIGATION_ITEM = 'platform.navigation.workshop'
const CATALOGUE_VIEW_KINDS = ['list', 'health', 'browse']

const payload = (pack, itemId) => pack.items?.find(item => item.id === itemId)?.content?.payload

/** Every object anywhere under `node` that declares an `editor`, with the path that reached it. */
function editorDeclarations(node, path = []) {
  if (node === null || typeof node !== 'object') return []
  if (Array.isArray(node)) return node.flatMap((entry, index) => editorDeclarations(entry, [...path, String(index)]))
  const here = typeof node.editor === 'object' && node.editor !== null ? [{path: path.join('.'), declaration: node}] : []
  return [...here, ...Object.entries(node).flatMap(([key, value]) => editorDeclarations(value, [...path, key]))]
}

/**
 * @param {{pack: unknown, exportDocuments: readonly string[]}} tree
 * @returns {readonly string[]} one finding per broken property, each naming the member.
 */
export function memberShellAndExportFindings({pack, exportDocuments}) {
  const findings = []

  // One export path. A member that exported its own package would add a second document here, and
  // the closure/manifest/digest boundary ADR 0097 decision 6 draws would have two owners.
  if (exportDocuments.length !== 1 || exportDocuments[0] !== PACK_PATH) {
    for (const document of exportDocuments.filter(document => document !== PACK_PATH)) {
      findings.push(`export path: ${document} is a second exported package; the shared export path is ${PACK_PATH}`)
    }
    if (!exportDocuments.includes(PACK_PATH)) findings.push(`export path: the shared exported package ${PACK_PATH} is absent`)
  }

  const register = payload(pack, REGISTER_ITEM)?.members
  if (!Array.isArray(register) || register.length === 0) {
    // Without the register there is no member list to enumerate, so every other answer below would
    // be vacuously green. Fail here rather than pass on an empty set.
    findings.push(`register: ${REGISTER_ITEM} declares no members, so no member can be checked`)
    return findings
  }
  const members = register.map(member => String(member.id))
  const workspace = payload(pack, WORKSPACE_ITEM)?.id
  const catalogue = payload(pack, CATALOGUE_VIEW_ITEM)
  const views = new Set((pack.items ?? []).map(item => item.content?.payload)
    .filter(view => view?.contentKind === 'ViewDefinition').map(view => view.definitionKey))

  for (const member of members) {
    for (const kind of CATALOGUE_VIEW_KINDS) {
      const id = `platform.${kind}.${member}`
      if (!views.has(id)) findings.push(`member ${member}: no ${kind} ViewDefinition ${id} on the shared export path`)
    }
  }

  // The editor destinations are released in one definition. Each member's editor lands with that
  // member's slice (ADR 0097 decision 7), so a member with no navigation entry is fine; but the
  // definition itself must be there, every entry must reach a destination and a surface, and every
  // destination mounts in the one shared shell with the shared projections.
  const sharedProjections = JSON.stringify(['react', 'blazor'])
  for (const {path, declaration} of editorDeclarations(catalogue ?? {})) {
    findings.push(`editor ${declaration.editor.id} at ${CATALOGUE_VIEW_ITEM}.${path} is declared outside the released navigation ${NAVIGATION_ITEM}`)
  }
  const navigation = payload(pack, NAVIGATION_ITEM)
  if (!Array.isArray(navigation?.navigation_entries) || navigation.navigation_entries.length === 0) {
    findings.push(`released navigation: ${NAVIGATION_ITEM} releases no navigation entries, so no editor destination can be checked`)
    return findings
  }
  const destinations = new Map((navigation.editor_destinations ?? []).map(destination => [String(destination.id), destination]))
  const surfaces = new Set((navigation.surface_catalogue?.surfaces ?? []).map(surface => String(surface.id)))
  if (navigation.workspace !== workspace) {
    findings.push(`released navigation: ${NAVIGATION_ITEM} mounts workspace ${navigation.workspace}, not the shared builder shell ${workspace}`)
  }
  for (const entry of navigation.navigation_entries) {
    const member = typeof entry.pillar === 'string' ? entry.pillar : `(no pillar on ${entry.id})`
    if (!members.includes(member)) {
      findings.push(`member ${member}: navigation entry ${entry.id} names no member in the register`)
      continue
    }
    const destination = destinations.get(String(entry.editor_destination_id))
    if (!destination) {
      findings.push(`member ${member}: navigation entry ${entry.id} names editor destination ${entry.editor_destination_id}, which ${NAVIGATION_ITEM} does not release`)
      continue
    }
    if (!surfaces.has(String(destination.surface_id))) {
      findings.push(`member ${member}: editor ${destination.id} opens surface ${destination.surface_id}, which the surface catalogue does not release`)
    } else if (String(destination.surface_id) !== String(destination.id)) {
      // Each editor is its own surface, so a link names the editor it opens (?surface=platform.editor.views).
      findings.push(`member ${member}: editor ${destination.id} opens surface ${destination.surface_id}, not its own surface ${destination.id}`)
    }
    if (JSON.stringify(destination.projections) !== sharedProjections) {
      findings.push(`member ${member}: editor ${destination.id} declares projections ${JSON.stringify(destination.projections)}, not the shared shell's ${sharedProjections}`)
    }
  }

  return findings
}

/** Exported package documents tracked under `_shared/packs`, repository-relative, POSIX separators. */
export function exportDocumentsUnder(root) {
  const packs = join(root, '_shared', 'packs')
  return readdirSync(packs, {withFileTypes: true, recursive: true})
    .filter(entry => entry.isFile() && entry.name.endsWith('.export.json'))
    .map(entry => `${resolve(entry.parentPath, entry.name).replaceAll('\\', '/').slice(root.replaceAll('\\', '/').length + 1)}`)
    .sort()
}

if (import.meta.main) {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
  const findings = memberShellAndExportFindings({
    pack: JSON.parse(readFileSync(join(root, PACK_PATH), 'utf8')),
    exportDocuments: exportDocumentsUnder(root),
  })
  for (const finding of findings) console.log(`FAIL ${finding}`)
  if (findings.length === 0) console.log('ok every member in the register reaches the shared builder shell and the shared export path')
  process.exit(findings.length === 0 ? 0 : 1)
}
