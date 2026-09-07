#!/usr/bin/env node
// Ticket 101 acceptance 3 — prove whether nullable public-surface parity is a family problem.
//
// This scanner is intentionally narrower than a general TypeScript-to-C# type comparer. Reference
// types cannot be classified from type shape alone under this repository's nullable-reference-type
// context, and guessing at related names would turn a useful parity check into a false-positive
// register. It reports only the value types and the Milliseconds naming convention from the ticket.
import {mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {extname, join, resolve} from 'node:path'

const argv = process.argv.slice(2)
// This tool lives inside the platform repository, so its default root is two levels above it.
const defaultPlatformRoot = resolve(`${import.meta.dirname}/../..`)
const platformRoot = resolve(argv.find(argument => !argument.startsWith('--'))
  ?? defaultPlatformRoot)

const VALUE_TYPES = new Set([
  'int', 'long', 'double', 'decimal', 'float', 'bool', 'TimeSpan', 'DateTime', 'DateTimeOffset',
  'Guid',
])

const PARAMETER = /\[Parameter\]\s*(?:\[[^\]]+\]\s*)*public\s+(?:(?:new|override|required|virtual)\s+)*(?<type>(?:global::)?(?:System\.)?[A-Za-z_]\w*\s*\??)\s+(?<name>[A-Za-z_]\w*)\s*\{/g

const lowerFirst = name => name.charAt(0).toLowerCase() + name.slice(1)
const escapeRegExp = value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

function reactPropAdmitsNull(source, name) {
  // Requiring null to be a union member avoids mistaking an object-literal value such as
  // `duration: null` for a public type declaration.
  const property = new RegExp(
    `(?:^|[\\n{;,])\\s*(?:readonly\\s+)?${escapeRegExp(name)}\\s*\\??\\s*:\\s*([^;\\n,}]*)`,
    'g',
  )
  return [...source.matchAll(property)].some(match => /(?:^|\|)\s*null\b/.test(match[1]))
}

export function compare(reactSource, blazorSource) {
  const findings = []
  for (const match of blazorSource.matchAll(PARAMETER)) {
    const declaredType = match.groups.type.replace(/\s+/g, '')
    if (declaredType.endsWith('?')) continue
    const typeName = declaredType.replace(/^global::/, '').replace(/^System\./, '')
    if (!VALUE_TYPES.has(typeName)) continue

    const names = [lowerFirst(match.groups.name)]
    if (match.groups.name.endsWith('Milliseconds')) {
      names.push(lowerFirst(match.groups.name.slice(0, -'Milliseconds'.length)))
    }
    const reactProp = names.find(name => reactPropAdmitsNull(reactSource, name))
    if (reactProp) findings.push({parameter: match.groups.name, blazorType: declaredType, reactProp})
  }
  return findings
}

function discoverModules(modules) {
  return Object.entries(modules)
    .filter(([id, module]) => id.startsWith('hlp.ui.')
      && typeof module.projections?.react?.path === 'string'
      && typeof module.projections?.blazor?.path === 'string')
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([moduleId, module]) => ({
      moduleId,
      reactPath: module.projections.react.path,
      blazorPath: module.projections.blazor.path,
    }))
}

function readSources(directory, extensions) {
  const sources = []
  for (const entry of readdirSync(directory, {withFileTypes: true})
    .sort((left, right) => left.name.localeCompare(right.name))) {
    // Package installs and .NET compiler output are consumers of the public surface, not sources.
    if (entry.isDirectory() && ['node_modules', 'bin', 'obj'].includes(entry.name)) continue
    const file = join(directory, entry.name)
    if (entry.isDirectory()) sources.push(readSources(file, extensions))
    else if (entry.isFile() && extensions.has(extname(entry.name))) {
      sources.push(readFileSync(file, 'utf8'))
    }
  }
  return sources.join('\n')
}

function runScan(root) {
  const catalog = JSON.parse(readFileSync(join(root, 'catalog/modules.yaml'), 'utf8'))
  const modules = discoverModules(catalog.modules ?? {})
  const findings = []
  for (const module of modules) {
    const reactSource = readSources(resolve(root, module.reactPath), new Set(['.ts', '.tsx']))
    const blazorSource = readSources(resolve(root, module.blazorPath), new Set(['.razor', '.cs']))
    findings.push(...compare(reactSource, blazorSource)
      .map(finding => ({moduleId: module.moduleId, ...finding})))
  }
  return {checked: modules.length, findings}
}

if (argv.includes('--canary')) {
  const failures = []
  const assert = (name, expected, run) => {
    try {
      const actual = run()
      if (actual !== expected) failures.push(`${name}: got ${actual}, expected ${expected}`)
    } catch (error) {
      failures.push(`${name}: ${error.message}`)
    }
  }

  assert('non-nullable value divergence', 1, () => compare(
    'duration?: number | null',
    '[Parameter] public int DurationMilliseconds { get; set; } = 4000;',
  ).length)
  assert('nullable value agreement', 0, () => compare(
    'duration?: number | null',
    '[Parameter] public int? DurationMilliseconds { get; set; } = 4000;',
  ).length)
  assert('reference type ignored', 0, () => compare(
    'label?: string | null',
    '[Parameter] public string Label { get; set; } = "";',
  ).length)
  assert('hlp.ui module discovery', 1, () => discoverModules({
    'hlp.ui.canary': {projections: {react: {path: 'react'}, blazor: {path: 'blazor'}}},
    'hlp.kernel.canary': {projections: {react: {path: 'react'}, blazor: {path: 'blazor'}}},
  }).length)
  const sourceRoot = mkdtempSync(join(tmpdir(), 'nullable-parity-canary-'))
  try {
    const nested = join(sourceRoot, 'src', 'nested')
    mkdirSync(nested, {recursive: true})
    writeFileSync(join(nested, 'surface.ts'), 'x'.repeat(200_000))
    assert('nested source traversal', 200_000,
      () => readSources(sourceRoot, new Set(['.ts'])).length)
  } finally {
    rmSync(sourceRoot, {recursive: true, force: true})
  }

  if (failures.length) {
    process.stderr.write(`canary FAIL:\n${failures.map(line => `  ${line}`).join('\n')}\n`)
    process.exit(1)
  }
  process.stdout.write('canary OK — nullable value parity and paired hlp.ui module discovery proven\n')
  process.exit(0)
}

const {checked, findings} = runScan(platformRoot)
if (argv.includes('--json')) {
  process.stdout.write(`${JSON.stringify({generated: true, checked, findings}, null, 2)}\n`)
} else {
  process.stdout.write(`${checked} paired UI modules checked; ${findings.length} nullable parity divergence`
    + `${findings.length === 1 ? '' : 's'}\n`)
  for (const finding of findings) {
    process.stdout.write(`  ${finding.moduleId}: Blazor ${finding.blazorType} ${finding.parameter}`
      + ` cannot express null admitted by React ${finding.reactProp}\n`)
  }
}
process.exitCode = findings.length > 0 ? 1 : 0
