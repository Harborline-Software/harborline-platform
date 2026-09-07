// Pure policy predicates shared by validate-repository.mjs and its tests.
//
// These lived inside validate-repository.mjs alongside five inline mutation-check arrays that
// re-ran on every gate execution - twice, since catalog-preflight and catalog-final are the same
// script. They are unit tests, so they belong in tooling/tests where run-tooling-selftests.mjs
// runs them once. Extracting them here is what lets a test import them without executing the
// validator, which is a script with top-level side effects (ticket 099 item 6).
//
// Every function here is pure: no filesystem, no process, no module-level mutable state.

export const allowedPresentationDispositions = new Set(['visual', 'not-applicable'])

export function presentationPolicyErrors(moduleId, presentation) {
  if (!allowedPresentationDispositions.has(presentation?.disposition)) {
    return [`${moduleId}: presentation disposition must be visual or not-applicable`]
  }
  if (presentation.disposition === 'not-applicable'
    && (typeof presentation.rationale !== 'string' || presentation.rationale.trim().length < 20)) {
    return [`${moduleId}: nonvisual presentation disposition lacks a bounded rationale`]
  }
  return []
}

export function themingPolicyErrors(moduleId, presentation, theming) {
  if (presentation?.disposition === 'visual' && theming?.disposition !== 'required') {
    return [`${moduleId}: visual module must require the theming quality dimension`]
  }
  if (presentation?.disposition === 'not-applicable' && theming?.disposition !== 'not-applicable') {
    return [`${moduleId}: nonvisual component must mark theming not-applicable`]
  }
  return []
}

// Ticket 256 slice 5. The catalog's dependency edges are checked against the module set by
// validate-repository ("unknown dependency"), but each spec's own `dependencies[]` was checked by
// nothing, so a module-key rename could leave the spec-side edges naming a module that no longer
// exists and every gate stayed green.
//
// An ABSENT `dependencies` in a spec is the empty set, not "unchecked": the catalog always declares
// the array (validate-repository requires it), so the catalog is the shape both sides are compared
// in. Treating absent as unchecked was the silent bypass — deleting the key rather than
// mis-spelling an entry left the rename unguarded.
//
// Entries are either a bare id or `{moduleId}`; both spellings are in use today, so the comparison
// normalises to ids and compares as a sorted set.
export function interfaceDependencyMismatch(moduleId, catalogDependencies, interfaceDependencies) {
  const asIds = list => [...new Set((list ?? []).map(entry => (typeof entry === 'string' ? entry : entry?.moduleId)))].sort()
  const declared = asIds(catalogDependencies).join(',')
  const specified = asIds(interfaceDependencies).join(',')
  return declared === specified ? null : `${moduleId}: interface dependencies differ from the catalog`
}

export function requiresQualityProfile(module, presentation) {
  return module.moduleKind?.endsWith('-component') || presentation?.disposition === 'visual'
}

export function npmPublicDistributionAuthorized(manifest) {
  return manifest?.private !== true
}

export function nugetPublicDistributionAuthorized(source) {
  return /<HarborlinePublicDistributionAuthorized>\s*true\s*<\/HarborlinePublicDistributionAuthorized>/.test(source)
}

export function dependencylessVitestConfigError(local, content, hasPackageManifest) {
  if (!local.endsWith('/vitest.config.ts') || hasPackageManifest) return null
  return /\bfrom\s*["']vitest\/config["']/.test(content)
    ? `${local}: contribution config imports vitest/config without owning dependencies`
    : null
}

export function rootScopedThemeAliasError(local, content) {
  const reactModuleStyle = /^projections\/react\/ui\/hlp\.ui\.[^/]+\/src\/style\.css$/.test(local)
  const blazorModuleStyle = /^projections\/blazor\/ui\/hlp\.ui\.[^/]+\/wwwroot\/[^/]+\.css$/.test(local)
  if (!reactModuleStyle && !blazorModuleStyle) return null
  const rootBlocks = [...content.matchAll(/(?:^|})\s*:root\s*\{([^}]*)\}/gs)]
  const aliases = rootBlocks.flatMap(([, declarations]) =>
    [...declarations.matchAll(/(--hl-[\w-]+)\s*:\s*[^;]*\bvar\(\s*(--hl-[\w-]+)/g)])
  if (aliases.length === 0) return null
  return `${local}: module token aliases must resolve in component theme scope, not :root (${aliases.map(match => match[1]).join(', ')})`
}

// Module status was validated by NOTHING until 2026-08-25 -- only PROJECTION status was. So every UI
// module carried whatever string was typed when it was written, and any of them could have been
// changed to anything without a check noticing. A status that cannot be wrong is not a status; it is
// a comment.
export const allowedModuleStatuses = new Set([
  'extracted-candidate', 'gap-implemented', 'implemented',
  'implemented-package-consumer-green', 'draft-package-consumer-green', 'draft-native-green',
  'implemented-engine-substrate-package-green', 'implemented-gate-model-green',
])

// `implemented-gate-model-green` is the one status that is a CLAIM about evidence rather than a
// label, so it is the one checked against the evidence: the recorded ui-gate-model receipt must say
// every Tier-1 gate reads PASS or NOT-APPLICABLE for that module.
//
// `rows` is null when the receipt is absent, and that case fails CLOSED. An unverifiable claim is
// not a passing one, and a fresh clone with no receipt must not become the way a status escapes the
// evidence it names.
export function moduleStatusErrors(moduleId, status, rows, receiptPath) {
  if (!allowedModuleStatuses.has(status)) return [`${moduleId}: unknown module status ${status}`]
  if (status !== 'implemented-gate-model-green') return []
  if (rows === null || rows === undefined) {
    return [`${moduleId}: status implemented-gate-model-green but ${receiptPath} is absent, so the claim cannot be checked`]
  }
  const row = rows[moduleId]
  if (!row) return [`${moduleId}: status implemented-gate-model-green but the gate-model receipt has no row for it`]
  if (row.terminal) return []
  const blocking = (row.gates ?? []).filter(gate => gate.status !== 'PASS' && gate.status !== 'NOT-APPLICABLE')
  return [`${moduleId}: status implemented-gate-model-green but the gate-model receipt reports ${blocking.map(gate => `${gate.id}=${gate.status}`).join(', ')}`]
}

// Ticket 269. The provenance record carried `sourcePaths` and `sourceBlobs` -- the paths and git
// blob ids of the upstream source each projection derived from -- and NOTHING read them. They were
// cut against the retired pre-convergence authority repository, whose commit
// (sourceAuthority.commit) is not reachable in harborline-api and cannot be fetched, so they could
// not be validated and could not be corrected: of 477 blob entries, 2 matched the path's blob at
// any resolvable api commit, and 252 named paths that exist in no api commit at all. Re-pointing
// them at a current commit would have invented provenance rather than checked it, so the fields are
// dropped and this refuses their return. contentHashes -- which IS validated, against files in this
// repository -- carries the record's remaining promise.
export const retiredProvenanceFields = ['sourcePaths', 'sourceBlobs']

export function retiredProvenanceFieldErrors(records) {
  return (records ?? []).flatMap(record => retiredProvenanceFields
    .filter(field => Object.hasOwn(record ?? {}, field))
    .map(field => `${record.moduleId}: provenance field ${field} was retired by ticket 269 and must not return`))
}
