import assert from 'node:assert/strict'
import {mkdirSync, mkdtempSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'
import test from 'node:test'

import {authoritySelectors, emittedClassLiterals, uiClassVocabularyErrors, undefinedClasses} from '../ui-class-vocabulary.mjs'

const authority = `
.hl-breadcrumb { color: red; }
.hl-breadcrumb__text, .hl-breadcrumb__link:hover { color: blue; }
.hl-breadcrumb__text--current { font-weight: 600; }
.hl-breadcrumb--size-sm { font-size: 1rem; }
`

test('authority selectors are the BEM class names the stylesheet defines', () => {
  assert.deepEqual([...authoritySelectors(authority)].sort(), ['hl-breadcrumb', 'hl-breadcrumb--size-sm', 'hl-breadcrumb__link', 'hl-breadcrumb__text', 'hl-breadcrumb__text--current'])
})

test('emitted literals keep classes and interpolation prefixes and drop id builders', () => {
  const source = `
    <span class="@(current ? "hl-breadcrumb__text hl-breadcrumb__text--current" : "hl-breadcrumb__current")">
    className={\`hl-breadcrumb--size-\${size}\`} id={\`hl-breadcrumb-\${key}-header\`}
  `
  assert.deepEqual([...emittedClassLiterals(source, 'breadcrumb')].sort(), ['hl-breadcrumb--size-', 'hl-breadcrumb__current', 'hl-breadcrumb__text', 'hl-breadcrumb__text--current'])
})

test('the breadcrumb defect is an undefined class and a satisfied prefix is not', () => {
  const selectors = authoritySelectors(authority)
  assert.deepEqual(undefinedClasses(selectors, new Set(['hl-breadcrumb__current', 'hl-breadcrumb--size-', 'hl-breadcrumb__text--current', 'hl-breadcrumb__'])), ['hl-breadcrumb__current'])
  assert.deepEqual(undefinedClasses(selectors, new Set(['hl-breadcrumb--fill-'])), ['hl-breadcrumb--fill-'])
})

test('repository check reports offenders per lane, tolerates the baseline, and flags stale baseline rows', () => {
  const root = mkdtempSync(resolve(tmpdir(), 'ui-class-vocabulary-'))
  try {
    mkdirSync(resolve(root, 'specs/modules/ui/hlp.ui.breadcrumb'), {recursive: true})
    mkdirSync(resolve(root, 'projections/react/ui/hlp.ui.breadcrumb/src'), {recursive: true})
    mkdirSync(resolve(root, 'projections/blazor/ui/hlp.ui.breadcrumb'), {recursive: true})
    writeFileSync(resolve(root, 'specs/modules/ui/hlp.ui.breadcrumb/style.css'), authority)
    writeFileSync(resolve(root, 'projections/react/ui/hlp.ui.breadcrumb/src/Breadcrumb.tsx'), `className={classes('hl-breadcrumb__text', current ? 'hl-breadcrumb__text--current' : undefined)}`)
    writeFileSync(resolve(root, 'projections/blazor/ui/hlp.ui.breadcrumb/HarborlineBreadcrumb.razor'), `<span class="@(current ? "hl-breadcrumb__current" : "hl-breadcrumb__text")">`)
    const errors = uiClassVocabularyErrors(root, {})
    assert.equal(errors.length, 1)
    assert.match(errors[0], /hlp\.ui\.breadcrumb: blazor emits hl-breadcrumb__current in projections\/blazor\/ui\/hlp\.ui\.breadcrumb\/HarborlineBreadcrumb\.razor/)
    assert.deepEqual(uiClassVocabularyErrors(root, {'hlp.ui.breadcrumb blazor': ['hl-breadcrumb__current']}), [])
    const stale = uiClassVocabularyErrors(root, {'hlp.ui.breadcrumb blazor': ['hl-breadcrumb__current', 'hl-breadcrumb__gone']})
    assert.equal(stale.length, 1)
    assert.match(stale[0], /still tolerates hl-breadcrumb__gone/)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
})
