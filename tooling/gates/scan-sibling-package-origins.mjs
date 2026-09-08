#!/usr/bin/env node
// The @harborline-software namespace is repository-owned. A local package must never be fetched
// from the public registry: a clean CI store cannot contain an unpublished legacy tarball.
import {existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync, mkdirSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, join, relative, resolve} from 'node:path'

const LOCAL_PROTOCOL = /^(?:file:|workspace:|link:)/
const EXCLUDED_DIRECTORIES = new Set(['.git', 'node_modules', 'dist', 'coverage'])
const DEPENDENCY_SECTIONS = ['dependencies']

function walk(root, predicate) {
  const found = []
  for (const entry of readdirSync(root, {withFileTypes: true})) {
    if (EXCLUDED_DIRECTORIES.has(entry.name)) continue
    const path = join(root, entry.name)
    if (entry.isDirectory()) found.push(...walk(path, predicate))
    else if (predicate(entry.name)) found.push(path)
  }
  return found
}

function packageFiles(root) {
  return walk(root, name => name === 'package.json')
}

function localPackages(root) {
  const owned = new Map()
  for (const path of packageFiles(root)) {
    const manifest = JSON.parse(readFileSync(path, 'utf8'))
    if (typeof manifest.name === 'string' && manifest.name.startsWith('@harborline-software/')) {
      owned.set(manifest.name, path)
    }
  }
  return owned
}

function scanManifest(path, owned, root) {
  const manifest = JSON.parse(readFileSync(path, 'utf8'))
  const findings = []
  for (const section of DEPENDENCY_SECTIONS) {
    for (const [name, specifier] of Object.entries(manifest[section] ?? {})) {
      if (!owned.has(name) || typeof specifier !== 'string' || LOCAL_PROTOCOL.test(specifier)) continue
      findings.push({
        kind: 'bare-local-dependency',
        file: relative(root, path),
        section,
        package: name,
        specifier,
        localManifest: relative(root, owned.get(name)),
      })
    }
  }
  return findings
}

function pnpmRegistryFindings(path, root) {
  const lines = readFileSync(path, 'utf8').split('\n')
  const findings = []
  for (let index = 0; index < lines.length; index += 1) {
    const packageMatch = /^\s*'(@harborline-software\/[^@']+)@[^']+':\s*$/.exec(lines[index])
    if (!packageMatch) continue
    let end = index + 1
    while (end < lines.length && !/^\s{2}'/.test(lines[end])) end += 1
    const resolution = lines.slice(index + 1, end).find(line => /^\s{4}resolution:\s*/.test(line)) ?? ''
    if (resolution.includes('directory:') || resolution.includes('type: directory') || resolution.includes('file:')) continue
    findings.push({
      kind: 'registry-lock-resolution',
      file: relative(root, path),
      package: packageMatch[1],
      resolution: resolution.trim() || 'implicit registry resolution',
    })
  }
  return findings
}

function packageLockRegistryFindings(path, root) {
  const lock = JSON.parse(readFileSync(path, 'utf8'))
  const findings = []
  const packages = lock.packages ?? {}
  for (const [location, entry] of Object.entries(packages)) {
    const packageMatch = /node_modules\/(@harborline-software\/[^/]+)$/.exec(location.replaceAll('\\', '/'))
    if (packageMatch && typeof entry?.resolved === 'string' && entry.resolved.includes('registry.npmjs.org')) {
      findings.push({kind: 'registry-lock-resolution', file: relative(root, path), package: packageMatch[1], resolution: entry.resolved})
    }
  }
  return findings
}

export function scanSiblingPackageOrigins(root) {
  const absoluteRoot = resolve(root)
  const owned = localPackages(absoluteRoot)
  const findings = []
  for (const path of packageFiles(absoluteRoot)) findings.push(...scanManifest(path, owned, absoluteRoot))
  for (const path of walk(absoluteRoot, name => name === 'pnpm-lock.yaml')) findings.push(...pnpmRegistryFindings(path, absoluteRoot))
  for (const path of walk(absoluteRoot, name => name === 'package-lock.json')) findings.push(...packageLockRegistryFindings(path, absoluteRoot))
  return {checkedPackages: owned.size, findings}
}

function plantCanary() {
  const root = mkdtempSync(join(tmpdir(), 'sibling-package-origin-canary-'))
  const write = (file, body) => {
    const path = join(root, file)
    mkdirSync(dirname(path), {recursive: true})
    writeFileSync(path, body)
  }
  write('engine/package.json', JSON.stringify({name: '@harborline-software/rule-engine'}))
  write('authoring/package.json', JSON.stringify({
    name: '@harborline-software/rule-authoring',
    dependencies: {'@harborline-software/rule-engine': '0.1.0-alpha.0'},
  }))
  write('authoring/pnpm-lock.yaml', `lockfileVersion: '9.0'\n\npackages:\n\n  '@harborline-software/rule-engine@0.1.0-alpha.0':\n    resolution: {tarball: https://registry.npmjs.org/@harborline-software/rule-engine/-/rule-engine-0.1.0-alpha.0.tgz}\n`)
  return root
}

function runCanary() {
  const root = plantCanary()
  try {
    const {findings} = scanSiblingPackageOrigins(root)
    const bare = findings.find(finding => finding.kind === 'bare-local-dependency')
    const registry = findings.find(finding => finding.kind === 'registry-lock-resolution')
    if (!bare || !registry) throw new Error(`planted registry pin was not refused: ${JSON.stringify(findings)}`)
    process.stdout.write('canary OK — planted bare local dependency and registry pnpm resolution are refused\n')
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
}

if (import.meta.main) {
  if (process.argv.includes('--canary')) {
    runCanary()
  } else {
    const root = resolve(process.argv.slice(2).find(argument => !argument.startsWith('--')) ?? `${import.meta.dirname}/../..`)
    const result = scanSiblingPackageOrigins(root)
    if (process.argv.includes('--json')) process.stdout.write(`${JSON.stringify({generated: true, ...result}, null, 2)}\n`)
    else process.stdout.write(`${result.checkedPackages} local packages checked; ${result.findings.length} sibling package origin finding${result.findings.length === 1 ? '' : 's'}\n`)
    process.exitCode = result.findings.length === 0 ? 0 : 1
  }
}
