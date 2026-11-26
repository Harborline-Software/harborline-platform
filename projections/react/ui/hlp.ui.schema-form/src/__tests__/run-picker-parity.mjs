// Run from the repository root, passing an existing receipt directory.
import { spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import assert from 'node:assert/strict'

const output = resolve(process.argv[2])
const env = { ...process.env, HARBORLINE_PICKER_PARITY_OUT: output }
for (const [executable, args, cwd] of [
  [process.execPath, ['../hlp.ui.button/node_modules/vitest/vitest.mjs', 'run', '--config', 'vitest.config.ts', 'src/__tests__/SchemaForm.picker-parity.test.tsx'], 'projections/react/ui/hlp.ui.schema-form'],
  ['dotnet', ['test', 'projections/blazor/ui/hlp.ui.button.tests/Harborline.UIAdapters.Blazor.Tests.csproj', '--no-restore', '--filter', 'FullyQualifiedName~PE5_Shared_picker_corpus', '-v:q'], '.'],
]) {
  const result = spawnSync(executable, args, { cwd, env, encoding: 'utf8' })
  process.stdout.write(result.stdout ?? '')
  process.stderr.write(result.stderr ?? '')
  if (result.error) throw result.error
  assert.equal(result.status, 0, `${executable} must pass before parity comparison`)
}
const react = JSON.parse(readFileSync(resolve(output, 'react-actions.json'), 'utf8'))
const blazor = JSON.parse(readFileSync(resolve(output, 'blazor-actions.json'), 'utf8'))
assert.deepEqual(react, blazor)
console.log(`PE-5: ${react.length} cross-shell normalized action lists agree`)
