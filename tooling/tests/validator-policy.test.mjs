#!/usr/bin/env node
// The five mutation-check suites that used to run inside validate-repository.mjs.
//
// They are unit tests over pure predicates, and they were re-running on every gate execution -
// twice, since catalog-preflight and catalog-final are the same script. Here they run once, under
// run-tooling-selftests.mjs, where a failure names the assertion instead of pushing a string into
// the validator's error list (ticket 099 item 6).
//
// A fifth suite came along that the report never counted: blazorRootScopedThemeAliasMutationChecks
// was asserted but had no counter, so three of these assertions were invisible even to the
// four-counter report they sat beside.
import assert from 'node:assert/strict'
import {test} from 'node:test'
import {
  dependencylessVitestConfigError, interfaceDependencyMismatch,
  npmPublicDistributionAuthorized, nugetPublicDistributionAuthorized,
  presentationPolicyErrors, requiresQualityProfile, retiredProvenanceFieldErrors,
  rootScopedThemeAliasError, themingPolicyErrors,
} from '../validator-policy.mjs'

test('theme applicability policy', () => {
  assert.equal(presentationPolicyErrors('mutation.nonvisual', {disposition: 'not-applicable', rationale: 'too short'}).length, 1)
  assert.equal(themingPolicyErrors('mutation.visual', {disposition: 'visual'}, {disposition: 'not-applicable'}).length, 1)
  assert.equal(themingPolicyErrors('mutation.nonvisual', {disposition: 'not-applicable'}, {disposition: 'required'}).length, 1)
  assert.equal(requiresQualityProfile({moduleKind: 'runtime'}, {disposition: 'visual'}), true)
})

test('distribution policy', () => {
  assert.equal(npmPublicDistributionAuthorized({private: true}), false)
  assert.equal(npmPublicDistributionAuthorized({}), true)
  assert.equal(nugetPublicDistributionAuthorized('<HarborlinePublicDistributionAuthorized>false</HarborlinePublicDistributionAuthorized>'), false)
  assert.equal(nugetPublicDistributionAuthorized('<HarborlinePublicDistributionAuthorized>true</HarborlinePublicDistributionAuthorized>'), true)
})

test('contribution config dependency', () => {
  const path = 'projections/react/ui/hlp.ui.example/vitest.config.ts'
  assert.notEqual(dependencylessVitestConfigError(path, "import { defineConfig } from 'vitest/config'", false), null)
  assert.equal(dependencylessVitestConfigError(path, 'export default { test: {} }', false), null)
  assert.equal(dependencylessVitestConfigError(path, "import { defineConfig } from 'vitest/config'", true), null)
})

test('interface dependency parity', () => {
  // Equal sets in either spelling agree.
  assert.equal(interfaceDependencyMismatch('m', ['hlp.ui.cn', 'hlp.ui.locale-provider'],
    [{moduleId: 'hlp.ui.locale-provider'}, {moduleId: 'hlp.ui.cn'}]), null)
  // Spec names a module the catalog does not.
  assert.notEqual(interfaceDependencyMismatch('m', ['hlp.ui.cn'], ['hlp.ui.cn', 'hlp.ui.bogus']), null)
  // Catalog edge absent from the spec.
  assert.notEqual(interfaceDependencyMismatch('m', ['hlp.ui.cn', 'hlp.ui.locale-provider'], ['hlp.ui.cn']), null)
  // Deleting the array is not a bypass: absent is the empty set.
  assert.notEqual(interfaceDependencyMismatch('m', ['hlp.ui.cn'], undefined), null)
  assert.equal(interfaceDependencyMismatch('m', [], undefined), null)
})

test('module token alias theme scope, React', () => {
  const path = 'projections/react/ui/hlp.ui.example/src/style.css'
  assert.notEqual(rootScopedThemeAliasError(path, ':root { --hl-example-muted: var(--hl-muted-foreground, #475569); }'), null)
  assert.equal(rootScopedThemeAliasError(path, '.hl-example { --hl-example-muted: var(--hl-muted-foreground, #475569); }'), null)
  assert.equal(rootScopedThemeAliasError('gallery/styles/canvas.css', ':root { --hl-example-muted: var(--hl-muted-foreground, #475569); }'), null)
})

test('module token alias theme scope, Blazor', () => {
  const path = 'projections/blazor/ui/hlp.ui.example/wwwroot/example.css'
  assert.notEqual(rootScopedThemeAliasError(path, ':root { --hl-example-muted: var(--hl-muted-foreground, #475569); }'), null)
  assert.equal(rootScopedThemeAliasError(path, '.hl-example { --hl-example-muted: var(--hl-muted-foreground, #475569); }'), null)
})

// Ticket 300: dialog, sheet, spotlight and window used to be exempted from this check because they
// carried :root theme aliases that the pairing rule could not yet express. 282 replaced those root
// blocks with component/portal scope, so the exemption set went dead - every entry named a file with
// no root alias behind it, which would have silently permitted a :root regression in exactly the
// files where the check is meant to be strictest. The exemption is gone; these four now get no
// special treatment, same as any other module stylesheet.
test('formerly pairing-constrained Blazor stylesheets have no exemption left', () => {
  for (const path of [
    'projections/blazor/ui/hlp.ui.dialog/wwwroot/dialog.css',
    'projections/blazor/ui/hlp.ui.sheet/wwwroot/sheet.css',
    'projections/blazor/ui/hlp.ui.spotlight/wwwroot/spotlight.css',
    'projections/blazor/ui/hlp.ui.window/wwwroot/window.css',
  ]) {
    assert.notEqual(rootScopedThemeAliasError(path, ':root { --hl-window-muted: var(--hl-muted, Canvas); }'), null,
      `${path}: reintroducing a :root alias must be caught now that no exemption covers it`)
  }
})

// Ticket 269: sourcePaths/sourceBlobs were retired because they could not be validated against any
// reachable upstream commit. This is the only thing standing between that decision and their quiet
// return in the next hand-edited record.
test('retired provenance source fields', () => {
  assert.deepEqual(retiredProvenanceFieldErrors([{moduleId: 'm', contentHashes: {}}]), [])
  assert.equal(retiredProvenanceFieldErrors([{moduleId: 'm', sourcePaths: []}]).length, 1)
  assert.equal(retiredProvenanceFieldErrors([{moduleId: 'm', sourceBlobs: {}}]).length, 1)
  // A value is not what makes it wrong -- the field's presence is, so an empty or null one still fails.
  assert.equal(retiredProvenanceFieldErrors([{moduleId: 'm', sourcePaths: null, sourceBlobs: null}]).length, 2)
  assert.deepEqual(retiredProvenanceFieldErrors(undefined), [])
})
