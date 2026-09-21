#!/usr/bin/env node
// T-585 item 1. The check itself runs against the released pack; these cases prove it BITES, which
// is the part a green run cannot show. Each mutation breaks exactly one property and the assertion
// reads the finding's text, because "a finding that names the member" is the acceptance, not "a
// finding happened".
import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {dirname, join, resolve} from 'node:path'
import {test} from 'node:test'
import {fileURLToPath} from 'node:url'
import {PACK_PATH, exportDocumentsUnder, memberShellAndExportFindings} from '../member-shell-and-export.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')
const released = () => JSON.parse(readFileSync(join(root, PACK_PATH), 'utf8'))
const item = (pack, id) => pack.items.find(entry => entry.id === id).content.payload
const findings = (pack, exportDocuments = [PACK_PATH]) => memberShellAndExportFindings({pack, exportDocuments})

test('the released pack puts every member on the shared shell and the shared export path', () => {
  assert.deepEqual(findings(released(), exportDocumentsUnder(root)), [])
})

test('the register is the member list, so a new member is covered without editing the check', () => {
  const pack = released()
  item(pack, 'platform-package-ck-6').members.push({id: 'ledgers', order: 14, pillar: 'ledgers'})
  assert.deepEqual(findings(pack), [
    'member ledgers: no list view platform.list.ledgers on the shared export path (platform-package-ck-7)',
    'member ledgers: no health view platform.health.ledgers on the shared export path (platform-package-ck-7)',
    'member ledgers: no browse view platform.browse.ledgers on the shared export path (platform-package-ck-7)',
  ])
})

test('a member whose view leaves the shared export path is named', () => {
  const pack = released()
  const views = item(pack, 'platform-package-ck-7').members
  views.splice(views.findIndex(view => view.id === 'platform.health.reports'), 1)
  assert.deepEqual(findings(pack), [
    'member reports: no health view platform.health.reports on the shared export path (platform-package-ck-7)',
  ])
})

test('a member that carries its own shell is named with the shell it carries', () => {
  const pack = released()
  item(pack, 'platform-package-ck-7').dataExchangeAuthoring.workspace = 'platform.workspace.data-exchange'
  assert.deepEqual(findings(pack), [
    'member data-exchanges: editor platform.editor.data-exchange mounts workspace platform.workspace.data-exchange,'
    + ' not the shared builder shell platform.workspace.workshop',
  ])
})

test('a member that builds its own projection of the shell is named', () => {
  const pack = released()
  item(pack, 'platform-package-ck-7').authoring.editor.projections = ['react']
  assert.deepEqual(findings(pack), [
    'member views: editor platform.editor.views declares projections ["react"], not the shared shell\'s ["react","blazor"]',
  ])
})

test('an editor that opens a surface its navigation entry does not name is a finding', () => {
  const pack = released()
  item(pack, 'platform-package-ck-7').authoring.navigationEntry.surface = 'platform.editor.views.legacy'
  assert.deepEqual(findings(pack), [
    'member views: navigation entry platform.navigation.views.author opens surface platform.editor.views.legacy,'
    + ' which is not its editor platform.editor.views',
  ])
})

test('an editor for a member outside the register is a finding', () => {
  const pack = released()
  item(pack, 'platform-package-ck-7').authoring.navigationEntry.pillar = 'inspectors'
  assert.deepEqual(findings(pack), [
    'member inspectors: editor platform.editor.views at platform-package-ck-7.authoring names no member in the register',
  ])
})

test('a second exported package is a second export path and is named', () => {
  assert.deepEqual(
    findings(released(), ['_shared/packs/platform/platform-pack.export.json', '_shared/packs/views/views-pack.export.json']),
    ['export path: _shared/packs/views/views-pack.export.json is a second exported package;'
      + ' the shared export path is _shared/packs/platform/platform-pack.export.json'],
  )
})

test('an empty register fails rather than passing over no members', () => {
  const pack = released()
  item(pack, 'platform-package-ck-6').members = []
  assert.deepEqual(findings(pack), [
    'register: platform-package-ck-6 declares no members, so no member can be checked',
  ])
})
