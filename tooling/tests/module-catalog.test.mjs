import assert from 'node:assert/strict'
import {mkdirSync, mkdtempSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'
import test from 'node:test'
import {fileURLToPath} from 'node:url'

import {canonicalJson, loadModuleCatalog, loadProvenance, rowFileErrors, splitCatalog, splitProvenance,
  writeModuleFile, writeProvenanceRecord} from '../module-catalog.mjs'
import {decide, plan} from '../migrate-catalog-branch.mjs'
import {selectRecords, staleHashes} from '../refresh-provenance-hashes.mjs'

const repositoryRoot = fileURLToPath(new URL('../..', import.meta.url))

const legacyCatalog = {
  schemaVersion: 1,
  repository: 'harborline-platform',
  modules: {
    'hlp.ui.zeta': {interface: {path: 'specs/modules/ui/hlp.ui.zeta/interface.yaml', revision: 1}, dependencies: []},
    'hlp.ui.alpha': {interface: {path: 'specs/modules/ui/hlp.ui.alpha/interface.yaml', revision: 2}, dependencies: ['hlp.ui.zeta']},
  },
  seams: {'ui.zeta.framework': {interfaceOwner: 'hlp.ui.zeta', status: 'conformant', adapters: ['react']}},
}
const legacyProvenance = {
  schemaVersion: 2,
  sourceAuthority: {repository: 'shipyard'},
  records: [{moduleId: 'hlp.ui.zeta', contentHashes: {'a.txt': 'x'}}, {moduleId: 'hlp.ui.alpha', contentHashes: {}}],
}

function scratch(build) {
  const root = mkdtempSync(resolve(tmpdir(), 'module-catalog-'))
  try {
    mkdirSync(resolve(root, 'catalog'), {recursive: true})
    mkdirSync(resolve(root, 'docs/provenance'), {recursive: true})
    const catalog = splitCatalog(legacyCatalog)
    const provenance = splitProvenance(legacyProvenance)
    writeFileSync(resolve(root, 'catalog/modules.yaml'), canonicalJson(catalog.header))
    writeFileSync(resolve(root, 'docs/provenance/source-map.yaml'), canonicalJson(provenance.header))
    for (const file of catalog.files.values()) writeModuleFile(root, file)
    for (const record of provenance.files.values()) writeProvenanceRecord(root, record)
    return build(root)
  } finally {
    rmSync(resolve(root), {recursive: true, force: true})
  }
}

test('the split catalog loads back as the single document, sorted, with seams owned by their module', () => {
  scratch(root => {
    const catalog = loadModuleCatalog(root)
    assert.deepEqual(Object.keys(catalog.modules), ['hlp.ui.alpha', 'hlp.ui.zeta'])
    assert.deepEqual(catalog.modules['hlp.ui.alpha'], legacyCatalog.modules['hlp.ui.alpha'])
    assert.deepEqual(catalog.seams, legacyCatalog.seams)
    assert.equal(catalog.repository, 'harborline-platform')
    assert.equal(Object.hasOwn(catalog, 'moduleDirectory'), false)
    assert.deepEqual(loadProvenance(root).records.map(record => record.moduleId), ['hlp.ui.alpha', 'hlp.ui.zeta'])
    assert.deepEqual(rowFileErrors(root), [])
  })
})

test('the validator refuses rows put back in a header, a misnamed row, a reflowed row and a doubly owned seam', () => {
  scratch(root => {
    writeFileSync(resolve(root, 'catalog/modules.yaml'), canonicalJson({...splitCatalog(legacyCatalog).header, modules: {}}))
    writeFileSync(resolve(root, 'catalog/modules/hlp.ui.beta.json'), canonicalJson({moduleId: 'hlp.ui.gamma', module: {}}))
    writeFileSync(resolve(root, 'catalog/modules/hlp.ui.alpha.json'),
      JSON.stringify({moduleId: 'hlp.ui.alpha', module: {}, seams: {'ui.zeta.framework': {status: 'conformant'}}}))
    const errors = rowFileErrors(root).join('\n')
    assert.match(errors, /modules belong in catalog\/modules/)
    assert.match(errors, /hlp\.ui\.beta\.json: holds "hlp\.ui\.gamma"/)
    assert.match(errors, /hlp\.ui\.alpha\.json: not canonical JSON/)
    assert.match(errors, /seam ui\.zeta\.framework is also declared by/)
  })
})

test('the real tree is split and canonical', () => {
  assert.deepEqual(rowFileErrors(repositoryRoot), [])
  const catalog = loadModuleCatalog(repositoryRoot)
  assert.ok(Object.keys(catalog.modules).length > 100)
  for (const seam of Object.values(catalog.seams)) assert.ok(Object.hasOwn(catalog.modules, seam.interfaceOwner))
})

test('migration applies only the rows a branch changed, and reports rows both sides changed', () => {
  assert.equal(decide({r: 1}, {r: 1}, {r: 5}).action, 'keep', 'untouched by the branch keeps the target row')
  assert.equal(decide({r: 1}, {r: 2}, {r: 1}).action, 'write', 'changed only by the branch takes the branch row')
  assert.equal(decide({r: 1}, {r: 2}, {r: 2}).action, 'keep', 'already equal needs nothing')
  assert.equal(decide(undefined, {r: 1}, undefined).action, 'write', 'a module the branch added is written')
  assert.equal(decide({r: 1}, undefined, {r: 1}).action, 'delete', 'a module the branch removed is removed')
  assert.equal(decide({r: 1}, {r: 2}, {r: 3}).action, 'conflict', 'both sides changed is never guessed')

  const base = splitProvenance(legacyProvenance).files
  const branch = new Map([...base].map(([id, record]) => [id, structuredClone(record)]))
  branch.get('hlp.ui.zeta').contentHashes['a.txt'] = 'branch-hash'
  const current = id => ({...structuredClone(base.get(id)), contentHashes: {'a.txt': 'main-hash'}})
  const steps = plan({catalog: {base: new Map(), branch: new Map()}, provenance: {base, branch}}, (kind, id) => current(id))
  assert.deepEqual(steps, [], 'hash values alone are recomputed afterwards, never merged')
})

test('the hash refresher finds stale and missing hashes and selects by module or changed path', () => {
  const records = [{moduleId: 'hlp.ui.a', contentHashes: {'README.md': 'stale', 'no/such/file': 'x'}}, {moduleId: 'hlp.ui.b', contentHashes: {}}]
  const stale = staleHashes(repositoryRoot, records[0])
  assert.deepEqual(stale.map(entry => [entry.path, entry.actual === null]), [['README.md', false], ['no/such/file', true]])
  assert.deepEqual(selectRecords(records, {modules: ['hlp.ui.b']}).map(record => record.moduleId), ['hlp.ui.b'])
  assert.deepEqual(selectRecords(records, {changedPaths: ['README.md']}).map(record => record.moduleId), ['hlp.ui.a'])
  assert.throws(() => selectRecords(records, {modules: ['hlp.ui.nope']}), /no provenance record for hlp\.ui\.nope/)
})
