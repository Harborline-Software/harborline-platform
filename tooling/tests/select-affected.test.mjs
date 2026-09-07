// Ticket 098 phase 3, selection half. The properties below are each a way the selector could quietly
// run the WRONG tests, which is worse than running all of them.

import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'
import test from 'node:test'

import {UnmappedPathError, dependentsFrom, ownershipFrom, pathMatchesPrefix, selectAffected}
  from '../gates/select-affected.mjs'

const root = resolve(import.meta.dirname, '../..')
const catalog = JSON.parse(readFileSync(resolve(root, 'catalog/modules.yaml'), 'utf8'))

test('a prefix lookalike does not match', () => {
  // hlp.ui.zetaish is not hlp.ui.zeta. A bare startsWith says it is, and a selector that picks the
  // wrong module is worse than one that picks them all.
  assert.equal(pathMatchesPrefix('projections/react/ui/hlp.ui.zetaish/x.ts', 'projections/react/ui/hlp.ui.zeta'), false)
  assert.equal(pathMatchesPrefix('projections/react/ui/hlp.ui.zeta/x.ts', 'projections/react/ui/hlp.ui.zeta'), true)
})

test('closure runs to DEPENDENTS, not dependencies', () => {
  // `dependencies` reads "A depends on B", so a change to B must select A. Following the field in
  // the direction it is named selects the changed module alone and skips everything that uses it.
  const dependents = dependentsFrom(catalog)
  const usersOfCn = dependents.get('hlp.ui.cn') ?? new Set()
  assert.ok(usersOfCn.size > 10, `hlp.ui.cn should have many dependents, got ${usersOfCn.size}`)

  const selected = selectAffected(catalog, ['projections/react/ui/hlp.ui.cn/src/index.ts'])
  assert.equal(selected.directModuleIds.length, 1)
  assert.ok(selected.moduleIds.length > selected.directModuleIds.length,
    'changing a widely-used module must reach its dependents')
})

test('a leaf module selects only itself', () => {
  const selected = selectAffected(catalog, ['specs/modules/ui/hlp.ui.badge/style.css'])
  assert.deepEqual(selected.directModuleIds, ['hlp.ui.badge'])
  assert.deepEqual(selected.moduleIds, ['hlp.ui.badge'])
})

test('an out-of-scope path selects nothing and is reported as ignored', () => {
  const selected = selectAffected(catalog, ['README.md', 'docs/adr/0001.md'])
  assert.deepEqual(selected.moduleIds, [])
  assert.equal(selected.ignoredPaths.length, 2)
})

test('a global input selects every module', () => {
  const all = Object.keys(catalog.modules).length
  for (const path of ['catalog/modules.yaml', 'gallery/styles/canvas.css', 'specs/modules/ui/baselines/sha256/a.png']) {
    assert.equal(selectAffected(catalog, [path]).moduleIds.length, all, `${path} must select all`)
  }
})

test('an unowned in-scope path selects the FULL suite and names itself', () => {
  // The dangerous direction. Ticket 098: "no reliable ownership mapping -> the full affected tier,
  // or the full suite. Never a silent skip." Selecting a subset here is the silent skip.
  const selected = selectAffected(catalog, ['specs/modules/ui/hlp.ui.nobody-owns-this/style.css'])
  assert.equal(selected.moduleIds.length, Object.keys(catalog.modules).length)
  assert.match(selected.reason, /hlp\.ui\.nobody-owns-this/)
  assert.equal(selected.unownedPaths.length, 1)
})

test('strict mode throws, so the ownership map can be audited', () => {
  assert.throws(
    () => selectAffected(catalog, ['specs/modules/ui/hlp.ui.nobody-owns-this/style.css'], {strict: true}),
    error => error instanceof UnmappedPathError && error.code === 'UNMAPPED_IN_SCOPE_PATH')
})

test('test-project and probe siblings are owned by their module', () => {
  // `<path>.tests` and `<path>.restart-probe` sit beside a projection, and segment-exact matching
  // rightly refuses to read them as the projection itself. Without an explicit sibling rule, 180
  // test projects fell through to "unowned" and every one of them selected the whole suite.
  const owners = ownershipFrom(catalog)
  const owns = prefix => owners.some(entry => entry.prefix === prefix)
  assert.ok(owns('projections/blazor/ui/hlp.ui.accordion.tests'), 'blazor test sibling must be owned')
  assert.ok(owns('projections/dotnet/blocks/hlp.blocks.workflow.restart-probe'), 'restart probe must be owned')
})

test('output is deduplicated and deterministically sorted', () => {
  const selected = selectAffected(catalog, [
    './specs/modules/ui/hlp.ui.badge/style.css',
    'specs/modules/ui/hlp.ui.badge/style.css',
    'specs/modules/ui/hlp.ui.alert/style.css',
  ])
  assert.deepEqual(selected.directModuleIds, ['hlp.ui.alert', 'hlp.ui.badge'])
})
