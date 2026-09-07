import assert from 'node:assert/strict'
import test from 'node:test'

import { npmPackageContributionErrors, reactPackageIdentity } from '../package-contribution-policy.mjs'

const build = modules => `const contributions = [\n${modules.map(moduleId => `  ['${moduleId.slice('hlp.ui.'.length)}', resolve(root, '../${moduleId}'), true],`).join('\n')}\n]\n`
const catalog = moduleIds => ({
  modules: Object.fromEntries(moduleIds.map(moduleId => [moduleId, {
    projections: { react: { packageContribution: reactPackageIdentity } },
  }])),
})

test('accepts catalog claims matching the aggregate root and explicit contributions', () => {
  assert.deepEqual(npmPackageContributionErrors(
    catalog(['hlp.ui.button', 'hlp.ui.card']),
    build(['hlp.ui.card']),
  ), [])
})

test('names a catalog claim absent from the aggregate build', () => {
  assert.deepEqual(npmPackageContributionErrors(
    catalog(['hlp.ui.button', 'hlp.ui.card', 'hlp.ui.schema-form']),
    build(['hlp.ui.card']),
  ), [
    'catalog-npm-contributions-match-aggregate-build: hlp.ui.schema-form claims @harborline-software/ui-react but is absent from the aggregate build',
  ])
})

test('names an aggregate contribution without a catalog claim', () => {
  assert.deepEqual(npmPackageContributionErrors(
    catalog(['hlp.ui.button']),
    build(['hlp.ui.card']),
  ), [
    'catalog-npm-contributions-match-aggregate-build: hlp.ui.card is present in the aggregate build but does not claim @harborline-software/ui-react',
  ])
})

test('fails closed when the contribution list syntax cannot be inventoried', () => {
  assert.match(
    npmPackageContributionErrors(catalog(['hlp.ui.button']), 'const contributions = deriveContributions()')[0],
    /unsupported shape/,
  )
})
