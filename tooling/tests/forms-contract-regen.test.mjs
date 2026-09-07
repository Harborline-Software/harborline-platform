// The forms projection manifest is the authority: `tooling/generate-authorization-bindings.mjs`
// stamps `manifest.model` into src/wire.ts and FormsJson.g.cs, and FormsContracts.g.cs declares
// the same constants by hand. Nothing on the gate route noticed when a generated file went
// stale, so renaming a manifest constant could land with three copies disagreeing (ticket 256
// slice 9). These assertions are the regen check: they run inside `tooling-selftests`, the
// phase-4 gate step (tooling/run-phase-4-gate.mjs).
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const read = (path) => readFileSync(join(root, path), 'utf8')
const manifest = JSON.parse(read('projections/typescript/contracts/hlp.contracts.forms/projection-manifest.json'))

test('src/wire.ts carries the committed manifest model verbatim', () => {
  assert.ok(read('projections/typescript/contracts/hlp.contracts.forms/src/wire.ts')
    .includes(`const model = ${JSON.stringify(manifest.model)}`),
  'wire.ts is stale — re-run `npm run generate:authorization`')
})

test('FormsJson.g.cs carries the committed manifest model verbatim', () => {
  assert.ok(read('projections/dotnet/contracts/hlp.contracts.identities/Forms/FormsJson.g.cs')
    .includes(JSON.stringify(manifest.model, null, 2)),
  'FormsJson.g.cs is stale — re-run `npm run generate:authorization`')
})

test('FormsContracts.g.cs declares every manifest constant by the manifest name and value', () => {
  const source = read('projections/dotnet/contracts/hlp.contracts.identities/Forms/FormsContracts.g.cs')
  const constants = Object.entries(manifest.model.declarations).filter(([, d]) => d.kind === 'constant')
  assert.ok(constants.length > 0, 'the manifest declares no constants — the scan shape changed')
  assert.equal(constants.length, manifest.surfaceCounts.constants, 'surfaceCounts.constants disagrees with the model')
  for (const [name, declaration] of constants) {
    assert.ok(source.includes(`public const string ${name} = ${JSON.stringify(declaration.value)};`),
      `FormsContracts.g.cs does not declare ${name} = ${JSON.stringify(declaration.value)}`)
    assert.ok(source.includes(`"${name}",`), `FormsContracts.g.cs does not list ${name} in its constant registry`)
  }
})

// `src/forms.ts` is the TypeScript authority the manifest was captured from
// (`generatedFrom.source`), and it was the one link in the chain nothing checked: renaming an
// export there left the manifest — and therefore both generated files — silently disagreeing
// (ticket 256 slice 9 review). These two close it.
test('forms.ts declares exactly the manifest declaration set', () => {
  const source = read('projections/typescript/contracts/hlp.contracts.forms/src/forms.ts')
  const exported = [...source.matchAll(/^export (?:interface|type|const) ([A-Za-z0-9_]+)/gm)].map(match => match[1])
  const declared = Object.keys(manifest.model.declarations)
  assert.ok(declared.length > 0, 'the manifest declares nothing — the scan shape changed')
  assert.equal(exported.length, manifest.surfaceCounts.declarations, 'surfaceCounts.declarations disagrees with forms.ts')
  assert.deepEqual([...exported].sort(), [...declared].sort(),
    'forms.ts and projection-manifest.json disagree — update the manifest and re-run `npm run generate:authorization`')
})

test('forms.ts declares every manifest constant by the manifest name and value', () => {
  const source = read('projections/typescript/contracts/hlp.contracts.forms/src/forms.ts')
  const constants = Object.entries(manifest.model.declarations).filter(([, d]) => d.kind === 'constant')
  assert.equal(constants.length, manifest.surfaceCounts.constants, 'surfaceCounts.constants disagrees with the model')
  for (const [name, declaration] of constants) {
    assert.ok(source.includes(`export const ${name} = ${JSON.stringify(declaration.value).replaceAll('"', "'")}`),
      `forms.ts does not declare ${name} = ${JSON.stringify(declaration.value)}`)
  }
})
