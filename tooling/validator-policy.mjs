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

// T-658. The phase-4 gate writes a failed step's captured output to .claude/gate-evidence/<step>.log
// precisely so it can be READ after the run has stopped. The validator walks the working tree, so
// that log then read as repository source and the .claude ban failed the NEXT run at
// catalog-preflight -- `prohibited source path .claude/gate-evidence/<step>.log` -- before a single
// real step ran. Two lanes lost time to it in one night, each seeing an unrelated failure in a
// different step than the one that had actually failed.
//
// The gate's own evidence is not repository content, so it is not scanned at all: the content rules
// would also fire on paths and imports quoted in a captured stack. The exemption is exactly the
// shape the gate writes and nothing else -- a single `.log` directly inside that one directory --
// so anything nested below it, any other extension, and every other path under .claude/ stay banned.
export function isGateFailureEvidence(localPath) {
  return /^\.claude\/gate-evidence\/[^/]+\.log$/.test(localPath)
}

// Approved bounded error-contract pilot. Only an explicit errorContract opts a row in; neither
// legacy code spelling nor a code prefix implies metadata, a category or a retry instruction.
// Canonical sources are supplied as text by the existing interface loader, never executed here.
export function errorContractPolicyErrors(moduleId, rows, canonicalSources = new Map()) {
  if (!Array.isArray(rows)) return []
  const errors = []
  const identities = new Map()
  const canonicalIdentities = new Map()
  for (const [index, row] of rows.entries()) {
    if (!errorContractObject(row) || !Object.hasOwn(row, 'errorContract')) continue
    const label = `${moduleId}: errors[${index}].errorContract`
    const add = message => errors.push(`${label}: ${message}`)
    const contract = row.errorContract
    if (!errorContractObject(contract)) {
      add('must be an object when declared')
      continue
    }
    errorContractKeys(contract, ['owner', 'canonicalSource', 'meaning', 'recovery', 'pointer', 'target', 'stage', 'presentation'], add)
    for (const [name, value] of [['code', row.code], ['when', row.when], ['owner', contract.owner], ['meaning', contract.meaning]]) {
      if (!errorContractText(value)) add(`${name} must be nonblank text`)
    }

    const reference = errorContractSourceReference(contract.canonicalSource)
    if (!reference) {
      add('canonicalSource must be a safe repository-relative .cs#Class.Member reference')
    } else {
      const source = canonicalSources instanceof Map ? canonicalSources.get(reference.path) : undefined
      const literal = typeof source === 'string'
        ? errorContractDirectLiteral(source, reference.type, reference.member) : null
      if (literal === null) add('canonicalSource must identify one readable, unambiguous direct string literal')
      else if (literal !== row.code) add('code differs from the exact canonical source literal')
    }

    const recovery = contract.recovery
    if (!errorContractObject(recovery)) add('recovery must declare category and action metadata')
    else {
      errorContractKeys(recovery, ['category', 'action'], add)
      if (!errorContractText(recovery.category) || !errorContractText(recovery.action)) add('recovery must declare nonblank category and action metadata')
    }

    const pointer = contract.pointer
    if (!errorContractObject(pointer) || !['required', 'optional', 'not-applicable'].includes(pointer.applicability)) {
      add('pointer must declare required, optional or not-applicable')
    } else {
      errorContractKeys(pointer, ['applicability', 'relativeTo', 'wholeDocument', 'examples', 'reason'], add)
      if (pointer.applicability === 'not-applicable') {
        if (!errorContractText(pointer.reason)) add('not-applicable pointer needs a reason')
        if (['relativeTo', 'wholeDocument', 'examples'].some(key => Object.hasOwn(pointer, key))) add('not-applicable pointer must omit document coordinates and examples')
      } else {
        if (!errorContractText(pointer.relativeTo)) add('pointer needs its document coordinate base')
        if (typeof pointer.wholeDocument !== 'boolean') add('pointer must declare wholeDocument explicitly')
        if (!Array.isArray(pointer.examples) || pointer.examples.length === 0) add('pointer needs explicit RFC6901 examples')
        else {
          if (pointer.examples.some(value => typeof value !== 'string' || (value !== '' && !value.startsWith('/')) || /~(?:[^01]|$)/.test(value))) add('pointer example is not an RFC6901 pointer')
          if (pointer.examples.includes('') !== pointer.wholeDocument) add('empty pointer example must agree with wholeDocument attribution')
        }
      }
    }

    for (const [name, applicable] of [['target', ['required', 'conditional']], ['stage', ['required', 'optional']]]) {
      const declaration = contract[name]
      if (!errorContractObject(declaration) || ![...applicable, 'not-applicable'].includes(declaration.applicability)) {
        add(`${name} has no supported applicability declaration`)
        continue
      }
      errorContractKeys(declaration, name === 'target' ? ['applicability', 'rule', 'reason'] : ['applicability', 'vocabulary', 'allowed', 'reason'], add)
      if (declaration.applicability === 'not-applicable') {
        if (!errorContractText(declaration.reason)) add(`not-applicable ${name} needs a reason`)
        const coordinates = name === 'target' ? ['rule'] : ['vocabulary', 'allowed']
        if (coordinates.some(key => Object.hasOwn(declaration, key))) add(`not-applicable ${name} must omit its applicable fields`)
      } else if (name === 'target') {
        if (!errorContractText(declaration.rule)) add('target needs its safe, authorized and fetchable disclosure rule')
      } else {
        if (!errorContractText(declaration.vocabulary)) add('stage needs its actual vocabulary')
        if (!Array.isArray(declaration.allowed) || declaration.allowed.length === 0 || declaration.allowed.some(value => !errorContractText(value))) add('stage needs explicit allowed values')
        else if (declaration.vocabulary === 'DefinitionAdmissionPhase'
          && declaration.allowed.some(value => !['Author', 'Publish', 'Install', 'Render'].includes(value))) add('stage is outside the declared DefinitionAdmissionPhase vocabulary')
      }
    }

    const presentation = contract.presentation
    if (!errorContractObject(presentation)) add('presentation needs a safe rule and empty arguments array')
    else {
      errorContractKeys(presentation, ['rule', 'arguments'], add)
      if (!errorContractText(presentation.rule)) add('presentation needs a safe rule')
      if (!Array.isArray(presentation.arguments) || presentation.arguments.length !== 0) add('this pilot admits only empty presentation arguments; no exception or payload context')
    }

    // Repeated declarations may mirror one canonical identity. Conflicting declared meanings are
    // errors; equal CLR literal values elsewhere and unannotated legacy rows are not scanned.
    const signature = errorContractSignature({when: row.when, contract})
    if (errorContractText(row.code)) {
      if (identities.has(row.code) && identities.get(row.code) !== signature) add('code has conflicting canonical identity or meaning metadata')
      identities.set(row.code, signature)
    }
    if (reference) {
      const identity = errorContractSignature({code: row.code, when: row.when, contract})
      if (canonicalIdentities.has(contract.canonicalSource) && canonicalIdentities.get(contract.canonicalSource) !== identity) add('canonical source has conflicting code or meaning metadata')
      canonicalIdentities.set(contract.canonicalSource, identity)
    }
  }
  return errors
}

function errorContractObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function errorContractText(value) {
  return typeof value === 'string' && value.trim().length > 0
}

function errorContractKeys(value, allowed, add) {
  if (Object.keys(value).some(key => !allowed.includes(key))) add('contains a field outside this bounded metadata pilot')
}

function errorContractSourceReference(value) {
  if (typeof value !== 'string') return null
  const match = /^((?:[A-Za-z0-9_-][A-Za-z0-9_.-]*\/)*[A-Za-z0-9_-][A-Za-z0-9_.-]*\.cs)#([A-Za-z_]\w*)\.([A-Za-z_]\w*)$/.exec(value)
  return match && match[0] === value ? {path: match[1], type: match[2], member: match[3]} : null
}

function errorContractDirectLiteral(source, type, member) {
  // Deliberately bounded data recognition: a top-level class with braces on their own lines and
  // one single-line, unescaped const string. Unsupported/computed/ambiguous shapes fail closed;
  // this is not a C# parser and it never evaluates an expression or resolves an alias itself.
  const uncommented = source.replace(/"(?:\\.|[^"\\\r\n])*"|\/\/[^\r\n]*|\/\*[\s\S]*?\*\//g,
    token => token.startsWith('"') ? token : token.replace(/[^\r\n]/g, ' '))
  if (/^\s*#/m.test(uncommented)) return null // Do not infer whether a C# declaration is active.
  if (/@"|"{3}/.test(uncommented)) return null // Verbatim/raw string bodies are outside this pilot.
  const classes = [...uncommented.matchAll(new RegExp(`^(?:(?:public|internal|static|sealed|partial|abstract)[ \\t]+)*class ${type}[ \\t]*\\r?\\n\\{\\r?\\n([\\s\\S]*?)^\\}`, 'gm'))]
  if (classes.length !== 1) return null
  const body = classes[0][1]
  if (/^[ \t]+(?:(?:public|internal|private|protected|static|sealed|partial|abstract)[ \t]+)*(?:class|struct|record|interface|enum)\b/m.test(body)) return null
  const declarations = [...body.matchAll(new RegExp(`^[ \\t]+(?:(?:public|internal|private|protected)[ \\t]+)+const[ \\t]+string[ \\t]+${member}[ \\t]*=[ \\t]*([^\\r\\n]+)$`, 'gm'))]
  if (declarations.length !== 1) return null
  return /^"([^"\\\r\n]*)";[ \t]*$/.exec(declarations[0][1])?.[1] ?? null
}

function errorContractSignature(value) {
  if (Array.isArray(value)) return `[${value.map(errorContractSignature).sort().join(',')}]`
  if (errorContractObject(value)) return `{${Object.keys(value).sort().map(key => `${JSON.stringify(key)}:${errorContractSignature(value[key])}`).join(',')}}`
  return JSON.stringify(value)
}
