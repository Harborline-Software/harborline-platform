import assert from 'node:assert/strict'
import {mkdirSync, mkdtempSync, readdirSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, resolve} from 'node:path'
import test from 'node:test'
import {fileURLToPath} from 'node:url'

import {laneOnlySelectors, pendingBlazorStylesheetReconciliation, pendingSetErrors} from '../sync-ui-spec-authority.mjs'

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const authority = '.hl-demo { color: red; }\n.hl-demo__body { color: blue; }\n'

// A throwaway repository with one module; `lane` is its Blazor wwwroot stylesheet.
function withFixture(lane, assertions) {
  const root = mkdtempSync(resolve(tmpdir(), 'pending-set-'))
  try {
    mkdirSync(resolve(root, 'specs/modules/ui/hlp.ui.demo'), {recursive: true})
    mkdirSync(resolve(root, 'projections/blazor/ui/hlp.ui.demo/wwwroot'), {recursive: true})
    writeFileSync(resolve(root, 'specs/modules/ui/hlp.ui.demo/style.css'), authority)
    writeFileSync(resolve(root, 'projections/blazor/ui/hlp.ui.demo/wwwroot/demo.css'), lane)
    assertions(root)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
}

test('a module listed on the pending set whose lane stylesheet no longer drifts is an error', () => {
  withFixture(authority, root => {
    const errors = pendingSetErrors(root, ['hlp.ui.demo'], new Set(['hlp.ui.demo']))
    assert.equal(errors.length, 1)
    assert.match(errors[0], /^hlp\.ui\.demo: pending-set drift is stale;/)
  })
})

test('an extra selector that sits in a comment is prose, not drift', () => {
  withFixture(`/* the alias block is declared on .hl-demo__ghost rather than :root */\n${authority}`, root => {
    assert.deepEqual(laneOnlySelectors(root, 'hlp.ui.demo'), [])
    assert.equal(pendingSetErrors(root, ['hlp.ui.demo'], new Set(['hlp.ui.demo'])).length, 1)
  })
})

test('a module that drifts but is not listed on the pending set is an error', () => {
  withFixture(`${authority}.hl-demo__ghost { color: green; }\n`, root => {
    assert.deepEqual(laneOnlySelectors(root, 'hlp.ui.demo'), ['hl-demo__ghost'])
    const errors = pendingSetErrors(root, ['hlp.ui.demo'], new Set())
    assert.equal(errors.length, 1)
    assert.match(errors[0], /^hlp\.ui\.demo: pending-set drift is unlisted; the Blazor stylesheet carries hl-demo__ghost,/)
    assert.deepEqual(pendingSetErrors(root, ['hlp.ui.demo'], new Set(['hlp.ui.demo'])), [])
  })
})

test('a pending-set entry that names no UI Spec module is an error', () => {
  withFixture(`${authority}.hl-demo__ghost { color: green; }\n`, root => {
    assert.deepEqual(pendingSetErrors(root, ['hlp.ui.demo'], new Set(['hlp.ui.demo', 'hlp.ui.gone'])),
      ['hlp.ui.gone: pending-set entry names no UI Spec module; remove it from pendingBlazorStylesheetReconciliation.'])
  })
})

test('the repository pending set is exact: every listed module drifts and no unlisted module does', () => {
  const moduleIds = readdirSync(resolve(repositoryRoot, 'specs/modules/ui')).filter(name => name.startsWith('hlp.ui.')).sort()
  assert.ok(moduleIds.length > 0)
  assert.deepEqual(pendingSetErrors(repositoryRoot, moduleIds), [])
  for (const moduleId of pendingBlazorStylesheetReconciliation) assert.ok(laneOnlySelectors(repositoryRoot, moduleId).length > 0)
})
