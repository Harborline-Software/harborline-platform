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
import {NAVIGATION_ITEM, PACK_PATH, exportDocumentsUnder, memberShellAndExportFindings} from '../member-shell-and-export.mjs'

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
    'member ledgers: no list ViewDefinition platform.list.ledgers on the shared export path',
    'member ledgers: no health ViewDefinition platform.health.ledgers on the shared export path',
    'member ledgers: no browse ViewDefinition platform.browse.ledgers on the shared export path',
  ])
})

test('a member whose view leaves the shared export path is named', () => {
  const pack = released()
  pack.items.splice(pack.items.findIndex(view => view.content.payload.definitionKey === 'platform.health.reports'), 1)
  assert.deepEqual(findings(pack), [
    'member reports: no health ViewDefinition platform.health.reports on the shared export path',
  ])
})

test('a view with the right item id but a wrong definitionKey or content kind does not cover the member', () => {
  for (const [property, value] of [['definitionKey', 'platform.health.other'], ['contentKind', 'FormDefinition']]) {
    const pack = released()
    item(pack, 'platform.health.reports')[property] = value
    assert.deepEqual(findings(pack), [
      'member reports: no health ViewDefinition platform.health.reports on the shared export path',
    ])
  }
})

const navigation = pack => item(pack, NAVIGATION_ITEM)
const destination = (pack, id) => navigation(pack).editor_destinations.find(entry => entry.id === id)

test('a released navigation that mounts its own shell is named with the shell it carries', () => {
  const pack = released()
  navigation(pack).workspace = 'platform.workspace.data-exchange'
  assert.deepEqual(findings(pack), [
    'released navigation: platform.navigation.workshop mounts workspace platform.workspace.data-exchange,'
    + ' not the shared builder shell platform.workspace.workshop',
  ])
})

test('a member that builds its own projection of the shell is named', () => {
  const pack = released()
  destination(pack, 'platform.editor.views').projections = ['react']
  assert.deepEqual(findings(pack), [
    'member views: editor platform.editor.views declares projections ["react"], not the shared shell\'s ["react","blazor"]',
  ])
})

test('a navigation entry whose editor destination is missing is a finding', () => {
  const pack = released()
  const destinations = navigation(pack).editor_destinations
  destinations.splice(destinations.findIndex(entry => entry.id === 'platform.editor.scheduling'), 1)
  assert.deepEqual(findings(pack), [
    'member schedules: navigation entry platform.navigation.schedules.author names editor destination platform.editor.scheduling,'
    + ' which platform.navigation.workshop does not release',
  ])
})

test('an editor that opens a surface the catalogue does not release is a finding', () => {
  const pack = released()
  destination(pack, 'platform.editor.views').surface_id = 'platform.editor.views.legacy'
  assert.deepEqual(findings(pack), [
    'member views: editor platform.editor.views opens surface platform.editor.views.legacy, which the surface catalogue does not release',
  ])
})

test('a navigation entry for a member outside the register is a finding', () => {
  const pack = released()
  navigation(pack).navigation_entries[0].pillar = 'inspectors'
  assert.deepEqual(findings(pack), [
    'member inspectors: navigation entry platform.navigation.views.author names no member in the register',
  ])
})

test('the Rules entry opening the released surface of another editor is a finding', () => {
  const pack = released()
  destination(pack, 'platform.editor.rules').surface_id = 'platform.editor.views'
  assert.deepEqual(findings(pack), [
    'member standards: editor platform.editor.rules opens surface platform.editor.views, not its own surface platform.editor.rules',
  ])
})

test('the Rules entry naming a pillar outside the register is a finding', () => {
  const pack = released()
  navigation(pack).navigation_entries.find(entry => entry.id === 'platform.navigation.standards.author').pillar = 'rules'
  assert.deepEqual(findings(pack), [
    'member rules: navigation entry platform.navigation.standards.author names no member in the register',
  ])
})

test('a pack without the released navigation fails rather than passing over no editors', () => {
  const pack = released()
  pack.items = pack.items.filter(entry => entry.id !== NAVIGATION_ITEM)
  assert.deepEqual(findings(pack), [
    'released navigation: platform.navigation.workshop releases no navigation entries, so no editor destination can be checked',
  ])
})

test('an editor declared outside the released navigation is a second source and is named', () => {
  const pack = released()
  item(pack, 'platform-package-ck-7').authoring = {
    workspace: 'platform.workspace.workshop',
    navigationEntry: {id: 'platform.navigation.views.author', pillar: 'views', surface: 'platform.editor.views'},
    editor: {id: 'platform.editor.views', projections: ['react', 'blazor']},
  }
  assert.deepEqual(findings(pack), [
    'editor platform.editor.views at platform-package-ck-7.authoring is declared outside the released navigation platform.navigation.workshop',
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
