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
  const views = new Map((catalogue?.members ?? []).map(view => [String(view.id), view]))

  for (const member of members) {
    for (const kind of CATALOGUE_VIEW_KINDS) {
      const id = `platform.${kind}.${member}`
      const view = views.get(id)
      if (!view) findings.push(`member ${member}: no ${kind} view ${id} on the shared export path (${CATALOGUE_VIEW_ITEM})`)
      else if (String(view.pillar) !== member) findings.push(`member ${member}: view ${id} is exported under pillar ${view.pillar}`)
    }
  }

  // An editor is allowed to be absent -- ADR 0097 decision 7 lands each member's editor with that
  // member's slice. What is not allowed is an editor that mounts somewhere other than the one
  // shared shell: a different workspace, or a projection set only that member builds.
  const declarations = editorDeclarations(catalogue ?? {})
  const sharedProjections = JSON.stringify(['react', 'blazor'])
  for (const {path, declaration} of declarations) {
    const pillar = declaration.navigationEntry?.pillar
    const member = typeof pillar === 'string' ? pillar : `(no pillar at ${CATALOGUE_VIEW_ITEM}.${path})`
    if (!members.includes(member)) {
      findings.push(`member ${member}: editor ${declaration.editor.id} at ${CATALOGUE_VIEW_ITEM}.${path} names no member in the register`)
      continue
    }
    if (declaration.workspace !== workspace) {
      findings.push(`member ${member}: editor ${declaration.editor.id} mounts workspace ${declaration.workspace}, not the shared builder shell ${workspace}`)
    }
    if (JSON.stringify(declaration.editor.projections) !== sharedProjections) {
      findings.push(`member ${member}: editor ${declaration.editor.id} declares projections ${JSON.stringify(declaration.editor.projections)}, not the shared shell's ${sharedProjections}`)
    }
    if (declaration.navigationEntry?.surface !== declaration.editor.id) {
      findings.push(`member ${member}: navigation entry ${declaration.navigationEntry?.id} opens surface ${declaration.navigationEntry?.surface}, which is not its editor ${declaration.editor.id}`)
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
