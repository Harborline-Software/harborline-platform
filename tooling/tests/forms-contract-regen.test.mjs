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

test('FormsContracts.cs declares every manifest constant by the manifest name and value', () => {
  const source = read('projections/dotnet/contracts/hlp.contracts.identities/Forms/FormsContracts.cs')
  const constants = Object.entries(manifest.model.declarations).filter(([, d]) => d.kind === 'constant')
  assert.ok(constants.length > 0, 'the manifest declares no constants — the scan shape changed')
  assert.equal(constants.length, manifest.surfaceCounts.constants, 'surfaceCounts.constants disagrees with the model')
  for (const [name, declaration] of constants) {
    assert.ok(source.includes(`public const string ${name} = ${JSON.stringify(declaration.value)};`),
      `FormsContracts.cs does not declare ${name} = ${JSON.stringify(declaration.value)}`)
    assert.ok(source.includes(`"${name}",`), `FormsContracts.cs does not list ${name} in its constant registry`)
  }
})

test('Forms and its owner modules declare exactly the manifest declaration set', () => {
  const modules = ['forms', 'rules', 'text', 'presentation']
  const exported = modules.flatMap(name => [...read(`projections/typescript/contracts/hlp.contracts.forms/src/${name}.ts`)
    .matchAll(/^export (?:interface|type|const) ([A-Za-z0-9_]+)/gm)].map(match => match[1]))
  const declared = Object.keys(manifest.model.declarations)
  assert.ok(declared.length > 0, 'the manifest declares nothing — the scan shape changed')
  assert.equal(exported.length, manifest.surfaceCounts.declarations, 'surfaceCounts.declarations disagrees with the owner modules')
  assert.deepEqual([...exported].sort(), [...declared].sort(),
    'the owner modules and projection-manifest.json disagree — update the manifest and re-run `npm run generate:authorization`')
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
