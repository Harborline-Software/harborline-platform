import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { test } from 'node:test'

const root = resolve(import.meta.dirname, '../..')

test('app-shell consumer installs with frozen pnpm and never repairs its lock', () => {
  const source = readFileSync(resolve(root, 'tooling/verify-package-fixtures.mjs'), 'utf8')
    .split('function verifyAppShellCapability() {')[1].split('\ntry {')[0]

  assert.match(source, /run\('pnpm', \['install', '--frozen-lockfile', '--ignore-scripts'\], \{ cwd: npmConsumer \}\)/)
  assert.doesNotMatch(source, /run\('npm'|--no-frozen-lockfile|--lockfile-only|--package-lock=false/)
  assert.match(source, /cpSync\(resolve\(root, 'tests\/package-consumers\/appshell-npm'\), npmConsumer, \{ recursive: true \}\)/)
  assert.doesNotMatch(source, /writeFileSync\([^\n]*pnpm-lock\.yaml/)
  assert.ok(existsSync(resolve(root, 'tests/package-consumers/appshell-npm/pnpm-lock.yaml')), 'app-shell frozen install requires a committed lock')
})
