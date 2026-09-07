import assert from 'node:assert/strict'
import {copyFileSync, mkdirSync, mkdtempSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'
import {spawnSync} from 'node:child_process'
import test from 'node:test'

const repositoryRoot = resolve(import.meta.dirname, '../..')
// The tool imports its selector reader from ui-class-vocabulary.mjs, so the fixture root needs both.
const sourceModules = ['sync-ui-spec-authority.mjs', 'ui-class-vocabulary.mjs']

function divergence(pairKind) {
  const root = mkdtempSync(resolve(tmpdir(), 'ui-spec-authority-'))
  const moduleId = pairKind === 'blazor' ? 'hlp.ui.data-grid' : 'hlp.ui.example'
  const authority = pairKind !== 'scenarios.json'
    ? `specs/modules/ui/${moduleId}/style.css`
    : `specs/modules/ui/${moduleId}/scenarios.json`
  const derived = pairKind === 'blazor' ? `projections/blazor/ui/${moduleId}/wwwroot/lane.css` : pairKind === 'style.css'
    ? `projections/react/ui/${moduleId}/src/style.css`
    : `gallery/scenarios/${moduleId}.json`
  try {
    mkdirSync(resolve(root, 'tooling'), {recursive: true})
    mkdirSync(resolve(root, 'docs/provenance'), {recursive: true})
    mkdirSync(resolve(root, authority, '..'), {recursive: true})
    mkdirSync(resolve(root, derived, '..'), {recursive: true})
    for (const name of sourceModules) copyFileSync(resolve(repositoryRoot, 'tooling', name), resolve(root, 'tooling', name))
    writeFileSync(resolve(root, 'docs/provenance/source-map.yaml'), '{"records":[]}\n')
    writeFileSync(resolve(root, authority), 'authority\n')
    writeFileSync(resolve(root, derived), 'diverged\n')
    const result = spawnSync(process.execPath, [resolve(root, 'tooling/sync-ui-spec-authority.mjs'), '--check'], {
      cwd: root,
      encoding: 'utf8',
    })
    return {authority, derived, result}
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
}

for (const pairKind of ['style.css', 'scenarios.json', 'blazor']) {
  test(`authority check rejects a diverged ${pairKind} pair with repair direction`, () => {
    const {authority, derived, result} = divergence(pairKind)
    assert.equal(result.status, 1)
    assert.match(result.stdout, new RegExp(derived.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))
    assert.match(result.stdout, new RegExp(authority.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))
    assert.match(result.stdout, /edit .* then run npm run ui-specs:generate to refresh/)
  })
}
