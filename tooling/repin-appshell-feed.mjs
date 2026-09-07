#!/usr/bin/env node

import { readFileSync, writeFileSync } from 'node:fs'
import { spawnSync } from 'node:child_process'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const manifestPath = resolve(root, 'tooling/appshell-feed-provenance.json')
const [sha, ...extra] = process.argv.slice(2)

if (extra.length || !/^[0-9a-f]{40}$/.test(sha ?? '')) {
  process.stderr.write('usage: repin-appshell-feed.mjs <api-sha-40>\n')
  process.exit(2)
}

const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'))
for (const artifact of manifest.artifacts) {
  artifact.ref = sha
  artifact.sha256 = ''
}
writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`)

const environment = { ...process.env }
for (const artifact of manifest.artifacts) delete environment[artifact.env]

function runResolver(args) {
  const result = spawnSync(process.execPath, ['tooling/resolve-appshell-feed.mjs', ...args], {
    cwd: root,
    encoding: 'utf8',
    env: environment,
    maxBuffer: 64 * 1024 * 1024,
  })
  if (result.status !== 0) {
    process.stderr.write(`${result.stdout ?? ''}${result.stderr ?? ''}`)
    process.exit(result.status ?? 1)
  }
}

runResolver(['--record-blank-pins'])
runResolver([])
process.stdout.write(`app-shell feed pinned to ${sha}; both artifacts reproduce\n`)
