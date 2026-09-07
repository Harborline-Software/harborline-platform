#!/usr/bin/env node

import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import path from 'node:path'
import {fileURLToPath} from 'node:url'

import {resolveCommand} from '../../../tooling/resolve-command.mjs'
import {resolvePinnedDotnet} from '../../../tooling/resolve-dotnet.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..')
const typescriptRoot = path.join(root, 'projections/typescript/foundation/hlp.foundation.rule-runtime')
const dotnetProject = path.join(root, 'projections/dotnet/foundation/hlp.foundation.rule-runtime.tests/Harborline.Foundation.RuleEngine.Tests.csproj')
const artifacts = path.join(root, 'artifacts/rule-runtime')
const dotnet = resolvePinnedDotnet(root)

function run(executable, args, cwd = root) {
  const resolved = resolveCommand(executable, args)
  const result = spawnSync(resolved.executable, resolved.args, {
    cwd,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    env: {...process.env, CI: '1', NO_COLOR: '1'},
  })
  if (result.status !== 0) throw new Error(`${executable} ${args.join(" ")} failed
${result.stdout}
${result.stderr}`)
}

run('pnpm', ['exec', 'vitest', 'run', 'src/__tests__/conformance.test.ts'], typescriptRoot)
run(dotnet.executable, [
  'test', dotnetProject, '--configuration', 'Release',
  '--filter', 'FullyQualifiedName~ConformanceTests', '-v:minimal',
])

const typescript = JSON.parse(readFileSync(path.join(artifacts, 'ts-outcomes.json'), 'utf8'))
const integrity = JSON.parse(readFileSync(path.join(artifacts, 'dotnet-outcomes.json'), 'utf8'))
assert.deepEqual(integrity, typescript, 'TypeScript reactive and .NET integrity outcomes diverged')

process.stdout.write(`${JSON.stringify({schemaVersion: 1, moduleId: "hlp.foundation.rule-runtime", status: "PASS", cases: Object.keys(typescript).length, projections: ["typescript-reactive", "dotnet-integrity"], outcomeParity: "byte-identical-canonical-values"}, null, 2)}
`)
