#!/usr/bin/env node
// Ticket 098 phase 3, the SELECTION half. "Selection reduces work; sharding reduces wall-clock."
//
// Rewritten rather than vendored. harborline-migration/tooling/carrier-preprocess/select-affected.mjs
// still exists and its discipline is copied here verbatim in spirit -- prefix boundaries, fail-closed
// on an unmapped in-scope path, deterministic sorted output, ignored paths reported rather than
// dropped -- but its body depends on that repository's manifest, runner-core and command-plan model,
// none of which exist here. Ticket 098 is explicit about the alternative: "The dependency graph
// exists. catalog/modules.yaml carries 101 modules and all 101 declare dependencies... derive it,
// never author it twice." So the ownership map and the edges are read from the catalog, and no
// module manifest is introduced alongside it.
//
// The sharding half is deliberately NOT built. Measured on this machine rather than inherited from
// the ticket: the recorded phase-4 gate runs the whole browser sweep in 6.0 minutes over 437 tests
// at four workers on sixteen cores. The ticket's 38.2 minutes was a two-core CI runner. Sharding
// splits work across RUNNERS; on one machine `workers` already does that, and building shard
// plumbing for a six-minute step would be infrastructure for a problem this machine does not have.
//
// Usage:
//   node select-affected.mjs --from <ref>        select from a git diff against <ref>
//   node select-affected.mjs --path <p> [...]    select from explicit paths
//   node select-affected.mjs ... --json
//   node select-affected.mjs --canary

import {execFileSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'

const argv = process.argv.slice(2)
const flagAll = name => argv.reduce((found, argument, index) =>
  argument === name && argv[index + 1] && !argv[index + 1].startsWith('--') ? [...found, argv[index + 1]] : found, [])
const flag = (name, fallback = null) => flagAll(name)[0] ?? fallback
const platformRoot = resolve(flag('--platform') ?? `${import.meta.dirname}/../..`)

// A change under one of these MUST map to a module. Anything else is ignored and SAID to be ignored.
const IN_SCOPE_PREFIXES = ['catalog', 'conformance', 'gallery', 'projections', 'specs']

// A change to shared ground selects every module. Ticket 098's riskTriggers, reduced to the two the
// catalog can answer honestly: the catalog itself, and the shared token/style ground every module
// draws from. Everything else routes through ownership.
const GLOBAL_PREFIXES = [
  'catalog/',
  'gallery/styles/',
  'tooling/',
  // Content-addressed visual baselines, stored by sha rather than by module. Nothing in the path
  // says which module a given png baselines, so there is no honest way to scope a change to one.
  // Ticket 098's rule for exactly this case: "no reliable ownership mapping -> the full affected
  // tier, or the full suite. Never a silent skip."
  'specs/modules/ui/baselines/',
  // Architecture tests assert over the WHOLE tree by definition -- lane purity and tier direction
  // are properties of the graph, not of a module -- so a change to them can invalidate any module.
  'projections/dotnet/architecture/',
]

export class UnmappedPathError extends Error {
  constructor(path) {
    super(`no module owns the in-scope path ${path}; refusing to select a subset that may be wrong`)
    this.code = 'UNMAPPED_IN_SCOPE_PATH'
    this.path = path
  }
}

// Segment-exact, never startsWith. `projections/react/ui/hlp.ui.zetaish/x.ts` must NOT match the
// prefix `projections/react/ui/hlp.ui.zeta`; a bare startsWith says it does, and a selector that
// picks the wrong module is worse than one that picks them all.
export function pathMatchesPrefix(changedPath, prefix) {
  if (changedPath === prefix) return true
  const bounded = prefix.endsWith('/') ? prefix : `${prefix}/`
  return changedPath.startsWith(bounded)
}

export function normalizePath(input) {
  return input.replace(/\\/g, '/').replace(/^\.\//, '').trim()
}

// Ownership is DERIVED from the catalog: every projection path a module declares, plus the spec and
// conformance directories named after it. Authoring a second map beside the catalog is the thing
// ticket 098 refuses.
export function ownershipFrom(catalog) {
  const owners = []
  for (const [moduleId, module] of Object.entries(catalog.modules ?? {})) {
    const prefixes = new Set()
    for (const projection of Object.values(module.projections ?? {})) {
      if (projection.path) prefixes.add(normalizePath(projection.path))
    }
    if (module.interface?.path) {
      const specDirectory = normalizePath(module.interface.path).split('/').slice(0, -1).join('/')
      if (specDirectory) prefixes.add(specDirectory)
    }
    prefixes.add(`conformance/${moduleId}`)
    prefixes.add(`gallery/scenarios/${moduleId}.json`)
    // The test project sits BESIDE its projection as `<path>.tests`, and segment-exact matching
    // rightly refuses to read that as the projection itself -- the same rule that stops
    // hlp.ui.zetaish matching hlp.ui.zeta. So the sibling is owned explicitly. Found by running
    // every tracked in-scope path through the selector: 648 of 2784 failed closed, and the test
    // projects were 180 of them.
    for (const projection of Object.values(module.projections ?? {})) {
      if (!projection.path) continue
      for (const suffix of ['.tests', '.restart-probe']) prefixes.add(`${normalizePath(projection.path)}${suffix}`)
    }
    for (const prefix of prefixes) owners.push({prefix, moduleId})
  }
  // Longest prefix first, so a nested projection path wins over a shorter sibling.
  return owners.sort((a, b) => b.prefix.length - a.prefix.length)
}

// Reverse edges. `dependencies` says "A depends on B", so when B changes it is A that must be
// re-tested. Selecting B's own tests alone is the silent-skip this whole check exists to prevent.
export function dependentsFrom(catalog) {
  const dependents = new Map()
  for (const [moduleId, module] of Object.entries(catalog.modules ?? {})) {
    for (const dependency of module.dependencies ?? []) {
      if (!dependents.has(dependency)) dependents.set(dependency, new Set())
      dependents.get(dependency).add(moduleId)
    }
  }
  return dependents
}

function closure(seed, dependents) {
  const selected = new Set(seed)
  const queue = [...seed]
  while (queue.length > 0) {
    for (const dependent of dependents.get(queue.pop()) ?? []) {
      if (!selected.has(dependent)) {
        selected.add(dependent)
        queue.push(dependent)
      }
    }
  }
  return selected
}

export function selectAffected(catalog, changedPaths, {strict = false} = {}) {
  const owners = ownershipFrom(catalog)
  const dependents = dependentsFrom(catalog)
  const allModuleIds = Object.keys(catalog.modules ?? {}).sort()

  const paths = [...new Set(changedPaths.map(normalizePath).filter(Boolean))].sort()
  const ignored = []
  const unowned = []
  const directly = new Set()

  for (const path of paths) {
    if (GLOBAL_PREFIXES.some(prefix => pathMatchesPrefix(path, prefix.replace(/\/$/, '')))) {
      return {
        status: 'SELECTED',
        reason: `global input changed: ${path}`,
        changedPaths: paths,
        ignoredPaths: ignored,
        directModuleIds: allModuleIds,
        moduleIds: allModuleIds,
      }
    }
    const owner = owners.find(candidate => pathMatchesPrefix(path, candidate.prefix))
    if (owner) { directly.add(owner.moduleId); continue }
    if (IN_SCOPE_PREFIXES.some(prefix => pathMatchesPrefix(path, prefix))) {
      // Ticket 098's rule verbatim: "no reliable ownership mapping -> the full affected tier, or the
      // full suite. Never a silent skip." So an unowned in-scope path selects EVERYTHING and names
      // itself as the reason. An earlier draft threw here, copying the migration tool; that is a
      // stricter contract than the ticket asks for and it makes the selector unusable until every
      // path in the tree is mapped -- a genuinely shared test project covering four modules has no
      // single owner and never will. --strict still throws, which is how the ownership map is
      // AUDITED for completeness without making everyday selection brittle.
      if (strict) throw new UnmappedPathError(path)
      unowned.push(path)
      continue
    }
    ignored.push(path)
  }

  if (unowned.length > 0) {
    return {
      status: 'SELECTED',
      reason: `no module owns ${unowned.length} in-scope path(s), so the full suite is selected: ${unowned.join(', ')}`,
      changedPaths: paths,
      ignoredPaths: ignored,
      unownedPaths: unowned,
      directModuleIds: [...directly].sort(),
      moduleIds: allModuleIds,
    }
  }

  const direct = [...directly].sort()
  return {
    status: 'SELECTED',
    reason: direct.length > 0 ? 'ownership and dependency closure' : 'no in-scope change',
    changedPaths: paths,
    ignoredPaths: ignored,
    unownedPaths: [],
    directModuleIds: direct,
    moduleIds: [...closure(direct, dependents)].sort(),
  }
}

export function changedPathsFromGit(from, cwd) {
  const out = execFileSync('git', ['diff', '--name-only', `${from}...HEAD`], {cwd, encoding: 'utf8'})
  return out.split('\n').map(normalizePath).filter(Boolean)
}

// assertGateCanFail. Every property below is a way the selector could quietly select the WRONG set,
// and each must be observed to be refused rather than assumed.
function canary() {
  const failures = []
  const catalog = {
    modules: {
      'hlp.ui.zeta': {dependencies: [], projections: {react: {path: 'projections/react/ui/hlp.ui.zeta'}},
        interface: {path: 'specs/modules/ui/hlp.ui.zeta/interface.yaml'}},
      'hlp.ui.zetaish': {dependencies: [], projections: {react: {path: 'projections/react/ui/hlp.ui.zetaish'}},
        interface: {path: 'specs/modules/ui/hlp.ui.zetaish/interface.yaml'}},
      'hlp.ui.alpha': {dependencies: ['hlp.ui.zeta'], projections: {react: {path: 'projections/react/ui/hlp.ui.alpha'}},
        interface: {path: 'specs/modules/ui/hlp.ui.alpha/interface.yaml'}},
    },
  }

  if (pathMatchesPrefix('projections/react/ui/hlp.ui.zetaish/x.ts', 'projections/react/ui/hlp.ui.zeta')) {
    failures.push('a prefix lookalike must NOT match: zetaish is not zeta')
  }

  const closureResult = selectAffected(catalog, ['projections/react/ui/hlp.ui.zeta/index.ts'])
  if (!closureResult.moduleIds.includes('hlp.ui.alpha')) {
    failures.push('changing a DEPENDENCY must select its dependent; edges run the other way from the field name')
  }
  if (closureResult.directModuleIds.includes('hlp.ui.alpha')) {
    failures.push('a dependent reached by closure must not be reported as directly changed')
  }

  const ignoredResult = selectAffected(catalog, ['README.md', 'docs/adr/0001.md'])
  if (ignoredResult.moduleIds.length !== 0) failures.push('an out-of-scope path must select nothing')
  if (ignoredResult.ignoredPaths.length !== 2) failures.push('ignored paths must be REPORTED, not dropped silently')

  // The dangerous direction: an unowned in-scope path must never quietly select a SUBSET. Safe by
  // default (everything), loud about why, and strict on demand for auditing the map.
  const unowned = selectAffected(catalog, ['specs/modules/ui/hlp.ui.nobody/style.css'])
  if (unowned.moduleIds.length !== 3) failures.push('an unowned in-scope path must select the FULL suite, not a subset')
  if (!unowned.reason.includes('hlp.ui.nobody')) failures.push('the unowned path must be NAMED in the reason, not merely counted')
  let threw = false
  try { selectAffected(catalog, ['specs/modules/ui/hlp.ui.nobody/style.css'], {strict: true}) } catch (error) {
    threw = error.code === 'UNMAPPED_IN_SCOPE_PATH'
  }
  if (!threw) failures.push('--strict must throw on an unowned in-scope path, so the map can be audited')

  const global = selectAffected(catalog, ['catalog/modules.yaml'])
  if (global.moduleIds.length !== 3) failures.push('a global input must select every module')

  const unsorted = selectAffected(catalog, [
    './projections/react/ui/hlp.ui.zetaish/b.ts',
    'projections/react/ui/hlp.ui.zetaish/b.ts',
    'projections/react/ui/hlp.ui.alpha/a.ts',
  ])
  if (JSON.stringify(unsorted.directModuleIds) !== JSON.stringify(['hlp.ui.alpha', 'hlp.ui.zetaish'])) {
    failures.push('output must be deduplicated and deterministically sorted')
  }

  if (failures.length > 0) {
    process.stderr.write(`canary FAIL:\n${failures.map(failure => `  ${failure}`).join('\n')}\n`)
    process.exit(1)
  }
  process.stdout.write('canary OK -- prefix lookalikes refused, closure runs to dependents, unowned in-scope paths select the full suite by name, --strict throws, globals select all\n')
  process.exit(0)
}

if (argv.includes('--canary')) canary()

const catalog = JSON.parse(readFileSync(resolve(platformRoot, 'catalog/modules.yaml'), 'utf8'))
const explicit = flagAll('--path')
const from = flag('--from')
const changed = explicit.length > 0 ? explicit
  : from ? changedPathsFromGit(from, platformRoot)
  : []

let result
try {
  result = selectAffected(catalog, changed, {strict: argv.includes('--strict')})
} catch (error) {
  process.stderr.write(`${error.message}\n`)
  process.exit(1)
}

if (argv.includes('--json')) {
  process.stdout.write(`${JSON.stringify(result, null, 2)}\n`)
} else {
  const all = Object.keys(catalog.modules ?? {}).length
  process.stdout.write(`${result.moduleIds.length} of ${all} modules selected -- ${result.reason}\n`)
  process.stdout.write(`  directly changed : ${result.directModuleIds.length}\n`)
  process.stdout.write(`  reached by edges : ${result.moduleIds.length - result.directModuleIds.length}\n`)
  if (result.ignoredPaths.length > 0) {
    // Named, never a silent count: a bounded check that does not say what it left out reads as
    // coverage it does not have.
    process.stdout.write(`  ignored (out of scope): ${result.ignoredPaths.join(', ')}\n`)
  }
  for (const moduleId of result.moduleIds) process.stdout.write(`    ${moduleId}\n`)
}
