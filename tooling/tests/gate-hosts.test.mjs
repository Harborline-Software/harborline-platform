// 276: tooling/gate-hosts.json is the one place the gate's host-tool expectations live, so a chain
// preflight can read it instead of hard-coding a list that drifts from what the gate actually spawns.
// This test keeps the file honest in both directions: every executable literally spawned by the four
// gate-runner scripts must be named in the file's "gate" set, and every "gate" entry in the file must
// actually be spawned by one of those scripts — an unused entry is exactly as wrong as a missing one.

import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'
import test from 'node:test'

const root = resolve(import.meta.dirname, '../..')
const hosts = JSON.parse(readFileSync(resolve(root, 'tooling/gate-hosts.json'), 'utf8'))

// run()/start() take the executable in a different position depending on the file's own local
// `function run(...)` signature: (id, executable, ...) in the three gate-side runners, but
// (executable, ...) in prepare-galleries.mjs. execFileSync/spawnSync calls always put the
// executable first.
const GATE_FILES = [
  { file: 'run-phase-4-gate.mjs', callExecutableIndex: 1 },
  { file: 'run-native.mjs', callExecutableIndex: 1 },
  { file: 'run-gallery-gate.mjs', callExecutableIndex: 1 },
  { file: 'prepare-galleries.mjs', callExecutableIndex: 0 },
]

const EXECUTABLE_TOKEN = /'([a-zA-Z]+)'|"([a-zA-Z]+)"|process\.execPath|[a-zA-Z]+\.executable/
const NODE_LIKE = /process\.execPath/
const DOTNET_LIKE = /\.executable$/

function tokenToName(token) {
  if (NODE_LIKE.test(token)) return 'node'
  if (DOTNET_LIKE.test(token)) return 'dotnet'
  return token.replace(/['"]/g, '')
}

// Executables named as string literals passed to spawn/spawnSync/execFileSync/run/start, or the two
// logical names the runners resolve indirectly: process.execPath (node, spawning itself to recurse
// into run-native.mjs) and <var>.executable (the pinned dotnet resolved by resolve-dotnet.mjs).
function spawnedExecutables(source, callExecutableIndex) {
  const found = new Set()
  for (const m of source.matchAll(/\b(?:spawn|spawnSync|execFileSync)\(\s*(?:'([a-zA-Z]+)'|"([a-zA-Z]+)")/g)) {
    found.add(m[1] ?? m[2])
  }
  const argPattern = callExecutableIndex === 0
    ? /\b(?:run|start)\(\s*(process\.execPath|[a-zA-Z]+\.executable|'[a-zA-Z]+'|"[a-zA-Z]+")/g
    : /\b(?:run|start)\(\s*'[^']*'\s*,\s*(process\.execPath|[a-zA-Z]+\.executable|'[a-zA-Z]+'|"[a-zA-Z]+")/g
  for (const m of source.matchAll(argPattern)) {
    found.add(tokenToName(m[1]))
  }
  // taskkill is a win32-only cleanup call to the OS shell, not a gate-assumed dependency the host
  // must provision; it is deliberately excluded from the discovered set.
  found.delete('taskkill')
  return found
}

test('every executable spawned by the gate runners is named in gate-hosts.json', () => {
  const discovered = new Set()
  for (const {file, callExecutableIndex} of GATE_FILES) {
    const source = readFileSync(resolve(root, 'tooling', file), 'utf8')
    for (const name of spawnedExecutables(source, callExecutableIndex)) discovered.add(name)
  }
  const declared = new Set(hosts.tools.filter(t => t.spawnedBy.includes('gate')).map(t => t.name))

  const missingFromFile = [...discovered].filter(n => !declared.has(n))
  const missingFromCode = [...declared].filter(n => !discovered.has(n))

  assert.deepEqual(missingFromFile, [], `spawned by a gate runner but not declared "gate" in gate-hosts.json: ${missingFromFile.join(', ')}`)
  assert.deepEqual(missingFromCode, [], `declared "gate" in gate-hosts.json but not spawned by any gate runner: ${missingFromCode.join(', ')}`)
})

test('gate-hosts.json has a version constraint for every declared tool', () => {
  for (const tool of hosts.tools) {
    assert.ok(tool.version && Object.keys(tool.version).length > 0, `${tool.name} has no version constraint`)
  }
})
