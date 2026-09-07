#!/usr/bin/env node
// Generate projection stylesheets and gallery-scenario lane copies from UI Spec authority.
// Check mode is the default and is safe for gates. --write is the only mutating mode.
import {copyFileSync, existsSync, readFileSync, readdirSync} from 'node:fs'
import {basename, resolve} from 'node:path'

import {authoritySelectors} from './ui-class-vocabulary.mjs'

const root = resolve(import.meta.dirname, '..')

// Every Blazor lane stylesheet is a derived copy of the authority, exactly as the React copy is.
// A module listed here still carries selectors the authority never defines, so its markup and the
// authority have to be reconciled before the copy can be enforced. The set is EXACT and
// pendingSetErrors proves it: a listed module that no longer drifts fails the check, and so does a
// drifted module that is not listed. Remove a module here in the change that reconciles it.
// The set is EMPTY as of 282 train C: every Blazor lane stylesheet is now a byte-identical copy of
// its authority. The set stays, empty, because the exactness check is what keeps it that way - a
// module that drifts again fails immediately instead of quietly earning a new entry.
export const pendingBlazorStylesheetReconciliation = new Set([
])

// "Drifted", exactly as ticket 282 defines it: the module's Blazor wwwroot stylesheet carries at
// least one selector the authority stylesheet never defines. Comments are stripped first, because
// prose naming a class is not a selector - confirm-dialog sat on the set on the strength of one
// sentence in a stale header, with every rule already byte-identical to the authority.
const withoutComments = css => css.replaceAll(/\/\*[\s\S]*?\*\//g, ' ')

export function laneOnlySelectors(root, moduleId) {
  const authorityPath = resolve(root, `specs/modules/ui/${moduleId}/style.css`)
  const wwwroot = resolve(root, `projections/blazor/ui/${moduleId}/wwwroot`)
  if (!existsSync(authorityPath) || !existsSync(wwwroot)) return []
  const defined = authoritySelectors(withoutComments(readFileSync(authorityPath, 'utf8')))
  const laneOnly = new Set()
  for (const name of readdirSync(wwwroot).filter(name => name.endsWith('.css'))) {
    for (const selector of authoritySelectors(withoutComments(readFileSync(resolve(wwwroot, name), 'utf8')))) {
      if (!defined.has(selector)) laneOnly.add(selector)
    }
  }
  return [...laneOnly].sort()
}

export function pendingSetErrors(root, moduleIds, pending = pendingBlazorStylesheetReconciliation) {
  const errors = []
  for (const moduleId of moduleIds) {
    const laneOnly = laneOnlySelectors(root, moduleId)
    if (pending.has(moduleId) && laneOnly.length === 0) {
      errors.push(`${moduleId}: pending-set drift is stale; the module is listed in pendingBlazorStylesheetReconciliation but its Blazor stylesheet carries no selector specs/modules/ui/${moduleId}/style.css leaves undefined. Remove it from the set and refresh the lane copy with npm run ui-specs:generate.`)
    }
    if (!pending.has(moduleId) && laneOnly.length > 0) {
      errors.push(`${moduleId}: pending-set drift is unlisted; the Blazor stylesheet carries ${laneOnly.join(', ')}, which specs/modules/ui/${moduleId}/style.css never defines. Reconcile the markup and the authority, or list the module in pendingBlazorStylesheetReconciliation.`)
    }
  }
  for (const moduleId of pending) {
    if (!moduleIds.includes(moduleId)) errors.push(`${moduleId}: pending-set entry names no UI Spec module; remove it from pendingBlazorStylesheetReconciliation.`)
  }
  return errors
}

if (import.meta.main) {
  const write = process.argv.includes('--write')
  const unknown = process.argv.slice(2).filter(argument => argument !== '--check' && argument !== '--write')
  if (unknown.length > 0 || (write && process.argv.includes('--check'))) {
    process.stderr.write('usage: node tooling/sync-ui-spec-authority.mjs [--check | --write]\n')
    process.exit(2)
  }

  const specRoot = resolve(root, 'specs/modules/ui')
  const sourceMapPath = resolve(root, 'docs/provenance/source-map.yaml')
  const sourceMap = JSON.parse(readFileSync(sourceMapPath, 'utf8'))
  const provenance = new Map()
  for (const record of sourceMap.records ?? []) {
    for (const [path, hash] of Object.entries(record.contentHashes ?? {})) {
      provenance.set(path, {moduleId: record.moduleId, hash})
    }
  }

  function blazorStylesheets(moduleId) {
    const wwwroot = resolve(root, `projections/blazor/ui/${moduleId}/wwwroot`)
    if (pendingBlazorStylesheetReconciliation.has(moduleId) || !existsSync(wwwroot)) return []
    return readdirSync(wwwroot).filter(name => name.endsWith('.css')).map(name => `projections/blazor/ui/${moduleId}/wwwroot/${name}`)
  }

  const moduleIds = readdirSync(specRoot).filter(name => name.startsWith('hlp.ui.')).sort()
  const pairs = []
  for (const moduleId of moduleIds) {
    for (const [authority, derived] of [
      [`specs/modules/ui/${moduleId}/style.css`, `projections/react/ui/${moduleId}/src/style.css`],
      [`specs/modules/ui/${moduleId}/scenarios.json`, `gallery/scenarios/${moduleId}.json`],
      ...blazorStylesheets(moduleId).map(derived => [`specs/modules/ui/${moduleId}/style.css`, derived]),
    ]) {
      if (!existsSync(resolve(root, authority))) continue
      pairs.push({moduleId, authority, derived})
    }
  }

  const errors = pendingSetErrors(root, moduleIds)
  if (pairs.length === 0) errors.push('no promoted UI Spec authority artifacts were found')
  for (const pair of pairs) {
    const authorityPath = resolve(root, pair.authority)
    const derivedPath = resolve(root, pair.derived)
    if (!existsSync(derivedPath)) {
      errors.push(`${pair.derived}: derived lane target is missing for authority ${pair.authority}; edit ${pair.authority}, then run npm run ui-specs:generate to refresh ${pair.derived}`)
      continue
    }

    const laneProvenance = provenance.get(pair.derived)
    const specProvenance = provenance.get(pair.authority)
    if (laneProvenance) {
      errors.push(`${pair.derived}: provenance still points at the derived lane copy; repoint it to ${pair.authority}`)
    }
    if (laneProvenance && !specProvenance) {
      errors.push(`${pair.authority}: provenance authority entry is missing`)
    }

    if (write) copyFileSync(authorityPath, derivedPath)
    if (!readFileSync(authorityPath).equals(readFileSync(derivedPath))) {
      errors.push(`${pair.derived}: diverges from authority ${pair.authority}; edit ${pair.authority}, then run npm run ui-specs:generate to refresh ${pair.derived}`)
    }
  }

  const report = {
    status: errors.length === 0 ? 'PASS' : 'FAIL',
    mode: write ? 'write' : 'check',
    derivedSpecPairs: pairs.length,
    errors,
  }
  process.stdout.write(`${JSON.stringify(report, null, 2)}\n`)
  if (errors.length > 0) process.exit(1)
}
