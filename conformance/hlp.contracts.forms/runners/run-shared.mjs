#!/usr/bin/env node

import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import path from 'node:path'
import {fileURLToPath, pathToFileURL} from 'node:url'

import {resolveCommand} from '../../../tooling/resolve-command.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..')
const packageRoot = path.join(root, 'projections/typescript/contracts/hlp.contracts.forms')
const npmBuild = resolveCommand('npm', ['run', 'build'])
const build = spawnSync(npmBuild.executable, npmBuild.args, {cwd: packageRoot, encoding: 'utf8', env: {...process.env, CI: '1', NO_COLOR: '1'}})
if (build.status !== 0) throw new Error(`forms TypeScript build failed\n${build.stdout}\n${build.stderr}`)

const fixtures = JSON.parse(readFileSync(path.join(root, 'conformance/hlp.contracts.forms/fixtures.yaml'), 'utf8'))
const manifest = JSON.parse(readFileSync(path.join(packageRoot, 'projection-manifest.json'), 'utf8'))
const wire = await import(pathToFileURL(path.join(packageRoot, 'dist/index.js')).href)
const results = []

for (const fixture of fixtures.cases) {
  let actual
  if (fixture.operation === 'surface.exports') {
    actual = Object.fromEntries(Object.keys(fixture.expected).map(key =>
      [key, key === 'declarations' ? wire.formsWireSurface.declarations.length : manifest.surfaceCounts[key]]))
    assert.deepEqual(actual, fixture.expected)
  } else if (fixture.operation === 'closed.values') {
    if (fixture.type) actual = wire.formsWireSurface.closedValues[fixture.type]
    else actual = Object.fromEntries(Object.keys(fixture.expected).map(type => [type, wire.formsWireSurface.closedValues[type]]))
    assert.deepEqual(actual, fixture.expected)
  } else if (fixture.operation === 'constant.value') {
    actual = wire[fixture.type]
    assert.equal(actual, fixture.expected)
  } else if (fixture.operation === 'wire.read' || fixture.operation === 'wire.round-trip') {
    if (fixture.expectedError) {
      assert.throws(() => wire.readFormsWire(fixture.type, fixture.input), new RegExp(fixture.expectedError))
      actual = {error: fixture.expectedError}
    } else {
      actual = wire.readFormsWire(fixture.type, fixture.input)
      const expected = typeof fixture.expected === 'string' ? fixture.input : fixture.expected
      assert.deepEqual(actual, expected)
    }
  } else {
    throw new Error(`unknown forms fixture operation ${fixture.operation}`)
  }
  results.push({id: fixture.id, status: 'PASS'})
}

process.stdout.write(`${JSON.stringify({schemaVersion: 1, moduleId: fixtures.moduleId, status: 'PASS', projection: 'typescript', cases: results.length, results}, null, 2)}\n`)
